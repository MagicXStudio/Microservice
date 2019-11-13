# 如何使用分布式锁

原文链接：https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html

我在 [Redis](http://redis.io/) 网站上偶然的发现了一个被称为 [Redlock](http://redis.io/topics/distlock) 的算法。这个算法在 Redis 专题上宣称实现了可容错的分布式锁（或者又叫[租赁](https://pdfs.semanticscholar.org/a25e/ee836dbd2a5ae680f835309a484c9f39ae4e.pdf)），并在向正在使用分布式系统的用户请求反馈。这个算法本能地在我的脑海里敲起了警钟，所以我花时间想了一段时间并记下了下来。

由于他们已经有了[超过 10 个关于 Redlock 的依赖实现](http://redis.io/topics/distlock)，我不知道谁准备好依赖这个算法，我认为这是值得公开分享我的笔记。我不会讲 Redis 的其他方面，其他一些方面在[其他地方](https://aphyr.com/tags/Redis)早就讨论过了。

在我深入 Redlock 细节之前，我要说下我是很喜欢 Redis 的，并且我过去曾成功的将它用于生产。我认为它能很好的适合某些场景，如果你想共享一些瞬时的，近似的，服务于服务之间的数据快速变化等。如果你因为一些原因丢失了相关数据也没什么大问题。举例来说，一个好的使用案例是维护每个 IP 地址的请求计数（为了限速目的）以及为设置每个用户 ID 不同的 IP 地址（为了检测滥用）。

然而，Redis 最近开始进军数据管理区域，它对强一致性以及持久性的期望越来越高 — — 这让我很担心，因为 Redis 并不是为此设计的。可论证的，分布式锁是这些领域的其中之一。让我们更仔细的研究细节吧。

## 你使用分布式锁是为了什么？

锁的目的就是在一系列的节点，它们可能尝试去做相同的工作，锁确保了最终只会执行一个（至少是同一时刻只执行一个）。这个工作可能会写一些数据到共享存储系统中，并执行一些计算，调用外部 API 等等。在高层，这里有两个理由来解释为什么在分布式系统中你可能会想要锁：[高效或正确性](http://research.google.com/archive/chubby.html)。为了区分这两个情况，你可以回答如果锁失败将会发生什么：

- 高效：用锁来避免一些不必要的多次做相同的工作（例如一些昂贵的计算开销）。如果锁失败了，那么两个节点最后就会做相同的工作，结果就是要略提高了开销（你最后要比其他方式多花费 5 美分给到 AWS）或者略微麻烦（比如一个用户最后会受到相同的通知两次）。
- 正确性：用锁来防止并发进程互相干扰，并且会破坏你的系统的当前状态。如果锁失败了，那么两节点间就会并发的工作在相同的数据上，结果就是破坏的文件，数据丢失，永久的不一致，就好比给病人用药量不对，或是其他严重的问题。

这两个情况都需要锁，但是你必须非常清晰这两个，哪一个是你要处理的。

我同意如果你为了提高效率为目的而正在使用锁，那它是不必要，用 Redlock 带来的开销和复杂性，有 5 个 Redis 服务器正在运行并且检查是否有多个人占用了你的锁。你最好的选择是只使用单个 Redis 实例，主服务器崩溃了就会使用异步复制到备份实例。

如果你使用了单个 Redis 实例，如果你的 Redis 节点突然断电，当然会释放一些锁，或会发生其他错误的事情。但是如果你只使用锁来当作一个效率的优化方案，并且不经常发生断电，那么这都不是什么大问题。这里说的 “不是大问题” 的场景恰恰是 Redis 的闪光点。至少如果你正在依赖单独的 Redis 实例，它是非常清楚对每个人来说系统的锁看起来都是近似的，仅用于非关键用途。

在另一方面，Redlock 算法，它使用了 5 个备份和多数投票，咋眼一看，它是非常适合你的锁对于正确性是非常重要的。我在下面几节中同意它是不适合这个目的的。文章剩下的部分，我将假设你的锁对于正确性来讲是非常重要为前提，如果两个节点之间并发，它们都会占有相同的锁，这是很严重的 bug。

## 使用锁保护资源

我们暂时先把 Redlock 的细节放在一边，来讨论下如何在通用情况下使用分布式锁（依赖于使用的锁算法细节）。要记住在分布式系统中的锁与多线程应用程序中的锁不同，这是很重要的。这是一个更复杂的问题，因为不同的节点和网络能以各种方式失败。

举个例子，假设你有一个应用程序，一个客户端要在共享存储系统中更新一个文件（如 HDFS 或 S3）。这个客户端首先会占有锁，然后读取文件，并做一些改变，写回到被修改的文件，最终释放搜索。这个锁会防止在并发执行读-修改-写回 这个周期的两个客户端，其中一个会丢失更新。代码看起来就像这样：

```js
// 坏代码
function writeData(filename, data) {
    var lock = lockService.acquireLock(filename);
    if(!lock) {
        throw 'Failed to acquire lock';
    }
    
    try {
        var file = storage.readFile(filename);
        var updated = updateContents(file, data);
        storage.writeFile(filename, updated);
    } finally {
        lock.release();
    }
}
```

很不辛，即使你有一个完美的锁住服务，上面的代码还是坏的。下面的图标展示了你最后的数据是怎么被破坏的：