# Quorum

通过每个决定都需要多数人通过来避免两组服务器各自做决定。

## 问题

在分布式系统中，一个服务器无论何时做什么操作，它都需要确保在崩溃之际都要把操作的结果给到客户端。这是通过给集群中其它服务器备份来实现的。但是这又会导致一个问题：在原始服务器能够完全识别更新之前， 要有多少服务器确认复制呢。如果原始服务器等代太多的复制，那这样势必会响应延迟 —— 这会降低系统的活性。但是如果又不等待足够的复制，那么更新数据就会丢失 —— 安全故障。关键就是要在系统性能和系统持续性上做个平衡。

## 解决方案

集群同意当大多数节点确认更新时接收这个更新。我们把这个数字称为法定人数（number of quorum）。所以如果集群中有 5 个节点，那么我们就需要 3 个法定人数（对于一个有 n 个节点的集群，quorum = n/2 + 1）

quorum 也表明了有多少失败可以容忍 —— 它是集群大小减去 quorum。5 个节点的集群可以容忍 2 个节点失败。通常，如果我们想要容忍 'f' 故障，我们就需要一个 2f +1 大小的集群。

思考下面给出的例子需要的 quorum：

- **集群中服务器更新数据**。[高水位标记](https://martinfowler.com/articles/patterns-of-distributed-systems/high-watermark.html)是用来确保只有这个数据能保证在大多数服务器对于客户端是可用的。
- **Leader 选举**。在 [Leader 和 Follower](https://martinfowler.com/articles/patterns-of-distributed-systems/leader-follower.html) 中，只有获得了大多数服务器的投票才能被选择成为一个 leader。

## 决定在集群中的服务器的数量

集群只有在大多数服务器运行的情况下才能工作。在正在数据复制的系统中，有两个点需要考虑：

- 写操作的吞吐量

  每次写数据到集群的时间，它需要复制到多个服务器。每个服务器完成这个写操作都需要一些额外的开销。**数据写入的延迟与形成仲裁的服务器数量成正比。以及在下面看到的，将集群中的服务器数量增加一倍会将吞吐量降低到原来的一半。**

- 要能容忍的故障数量

  容忍故障数量是取决于集群中服务器数量的。但是只是添加超过一个服务器到三个服务器的集群，这是不会增加故障容忍量的。

思考以下两个因素，实际上大多数基于仲裁的系统有一个三到五个节点的集群。一个 5 个节点的集群能容忍 2 个故障，数据写吞吐量可以达到每秒几千个请求。

这里有关于如何选择基于故障容忍数量以及合适的高效的吞吐量的服务器数量的例子。吞吐量列显示的是近似的相对吞吐量，突出显示吞吐量如何随着服务器数量的增加而下降。数字因系统的不同而不同。作为例子，读者可以参考在 [Raft Thesis](https://web.stanford.edu/~ouster/cgi-bin/papers/OngaroPhD.pdf) 以及 [Zookeeper](https://www.usenix.org/legacy/event/atc10/tech/full_papers/Hunt.pdf) 原始论文实际发表的吞吐量。

| 服务器数量 | 仲裁 | 容忍故障数 | 吞吐量 |
| ---------- | ---- | ---------- | ------ |
| 1          | 1    | 0          | 100    |
| 2          | 2    | 0          | 85     |
| 3          | 2    | 1          | 82     |
| 4          | 3    | 1          | 57     |
| 5          | 3    | 2          | 48     |
| 6          | 4    | 2          | 41     |
| 7          | 4    | 3          | 36     |

## 例子

- 所有的一致性算法如 [Zab](https://zookeeper.apache.org/doc/r3.4.13/zookeeperInternals.html#sc_atomicBroadcast)、[Raft](https://raft.github.io/)、[Paxos](https://en.wikipedia.org/wiki/Paxos_(computer_science)) 都是基于 Quorum 的
- 甚至是没有一致性的系统中，在最近的服务器故障或网络分区的时候，quorum 也可以用来保证至少有一个服务器有最新的更新值。例如。在数据中如 Cassandra，它被配置只有在大多数服务器成功更新记录之后才会返回成功。