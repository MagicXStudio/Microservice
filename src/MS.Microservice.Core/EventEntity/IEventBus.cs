﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.EventEntity
{
    public interface IEventBus
    {
        void Publish(EventBase @event);
        Task PublishAsync<TEvent>(TEvent @event)
            where TEvent : EventBase;

        void Subscribe<TEvent, TEventHandle>()
            where TEvent : EventBase
            where TEventHandle : IEventHandle<TEvent>;
    }
}
