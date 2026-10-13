using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace LyricConnector.Probes
{
    public sealed class Notification
    {
        public byte[] Data { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public long ReceivedTimestamp { get; set; }
    }

    public sealed class NotificationCollector
    {
        private readonly ConcurrentQueue<Notification> notifications = new ConcurrentQueue<Notification>();

        public void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            var receivedAtUtc = DateTime.UtcNow;
            var receivedTimestamp = Stopwatch.GetTimestamp();
            var data = ReadNotification(eventArgs);
            notifications.Enqueue(new Notification
            {
                Data = data,
                ReceivedAtUtc = receivedAtUtc,
                ReceivedTimestamp = receivedTimestamp
            });
        }

        public Delegate CreateHandler(Type handlerType)
        {
            return Delegate.CreateDelegate(handlerType, this, GetType().GetMethod("OnValueChanged"));
        }

        public Notification[] Drain()
        {
            var result = new List<Notification>();
            Notification item;
            while (notifications.TryDequeue(out item)) result.Add(item);
            return result.ToArray();
        }

        private static byte[] ReadNotification(GattValueChangedEventArgs eventArgs)
        {
            using (var reader = DataReader.FromBuffer(eventArgs.CharacteristicValue))
            {
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                return data;
            }
        }

        public static byte[] ReadAttribute(GattReadResult result)
        {
            using (var reader = DataReader.FromBuffer(result.Value))
            {
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                return data;
            }
        }
    }
}
