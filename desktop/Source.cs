using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace LyricConnector
{
    internal static class WinRt
    {
        private static readonly MethodInfo asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods()
            .Single(method => method.Name == "AsTask" && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");

        public static async Task<T> Await<T>(object operation, CancellationToken cancellation)
        {
            var task = (Task<T>)asTask.MakeGenericMethod(typeof(T)).Invoke(null, new[] { operation });
            var limit = Task.Delay(TimeSpan.FromSeconds(15), cancellation);
            if (await Task.WhenAny(task, limit).ConfigureAwait(false) != task)
            {
                cancellation.ThrowIfCancellationRequested();
                throw new TimeoutException("手机连接超时，请检查蓝牙后重新连接。");
            }
            cancellation.ThrowIfCancellationRequested();
            return await task.ConfigureAwait(false);
        }

        public static IDisposable Subscribe(object source, string name, object target, string method)
        {
            return new Subscription(source, source.GetType().GetEvent(name), target, method);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly object source;
            private readonly EventInfo info;
            private readonly Delegate handler;
            private readonly object token;
            public Subscription(object source, EventInfo info, object target, string method)
            {
                this.source = source;
                this.info = info;
                handler = Delegate.CreateDelegate(info.EventHandlerType, target, target.GetType().GetMethod(method));
                token = info.GetAddMethod().Invoke(source, new object[] { handler });
            }
            public void Dispose() { info.GetRemoveMethod().Invoke(source, new[] { token }); }
        }
    }

    public sealed class DeviceChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }

    internal sealed class Packet
    {
        public byte Entity;
        public byte Attribute;
        public bool Truncated;
        public string Value;
        public long Timestamp;
    }

    internal static class AmsTrackMetadata
    {
        private static bool IsNetEasePlayer(string player)
        {
            return String.Equals(player, "NetEase Music", StringComparison.OrdinalIgnoreCase)
                || player == "网易云音乐";
        }

        private static bool IsQQMusicPlayer(string player)
        {
            return String.Equals(player, "QQMusic", StringComparison.OrdinalIgnoreCase)
                || String.Equals(player, "QQ Music", StringComparison.OrdinalIgnoreCase)
                || player == "QQ音乐";
        }

        private static bool UsesCompositeArtistTitleMetadata(string player)
        {
            return IsNetEasePlayer(player) || IsQQMusicPlayer(player);
        }

        internal static Track Normalize(string player, Track track, out bool titleFromArtist)
        {
            titleFromArtist = false;
            if (!UsesCompositeArtistTitleMetadata(player)) return track;
            var qqMusic = IsQQMusicPlayer(player);
            var separator = track.Artist.LastIndexOf(qqMusic ? '—' : '/');
            if (separator <= 0 || separator == track.Artist.Length - 1) return track;
            var first = track.Artist.Substring(0, separator).Trim();
            var second = track.Artist.Substring(separator + 1).Trim();
            var artist = qqMusic ? second : first;
            var title = qqMusic ? first : second;
            if (artist.Length == 0 || title.Length == 0) return track;
            titleFromArtist = true;
            return new Track { Title = title, Artist = artist, Album = track.Album, Duration = track.Duration };
        }

        internal static bool IsTitleStable(bool titleFromArtist, string first, string second)
        {
            return titleFromArtist || first == second;
        }

        internal static bool HasTrackChanged(Packet packet, Track track, string player, bool titleFromArtist)
        {
            if (packet.Entity != 2) return false;
            if (titleFromArtist && UsesCompositeArtistTitleMetadata(player))
            {
                if (packet.Attribute == 2) return false;
                if (packet.Attribute == 0)
                {
                    bool packetTitleFromArtist;
                    var packetTrack = Normalize(player, new Track { Title = "", Artist = packet.Value,
                        Album = track.Album, Duration = track.Duration }, out packetTitleFromArtist);
                    return packetTitleFromArtist
                        ? packetTrack.Artist != track.Artist || packetTrack.Title != track.Title
                        : packet.Value != track.Artist;
                }
            }
            if (packet.Attribute == 0) return packet.Value != track.Artist;
            if (packet.Attribute == 1) return packet.Value != track.Album;
            if (packet.Attribute == 2) return packet.Value != track.Title;
            if (packet.Attribute == 3)
            {
                double duration;
                return track.Duration > 0 && TryParseDuration(packet.Value, out duration) && Math.Abs(duration - track.Duration) > 2;
            }
            return false;
        }

        internal static bool HasTrackChanged(Track candidate, bool candidateTitleFromArtist, string player,
            Track current, bool currentTitleFromArtist, string currentPlayer)
        {
            if (player != currentPlayer) return true;
            if (candidate.Artist.Length > 0 && current.Artist.Length > 0 && candidate.Artist != current.Artist) return true;
            if (candidate.Album.Length > 0 && current.Album.Length > 0 && candidate.Album != current.Album) return true;
            if (candidateTitleFromArtist == currentTitleFromArtist && candidate.Title != current.Title) return true;
            return candidate.Duration > 0 && current.Duration > 0 && Math.Abs(candidate.Duration - current.Duration) > 2;
        }

        internal static bool TryParseDuration(string value, out double duration)
        {
            double parsed;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || Double.IsNaN(parsed) || Double.IsInfinity(parsed) || parsed <= 0)
            {
                duration = 0;
                return false;
            }
            duration = parsed;
            return true;
        }
    }

    internal sealed class RemoteCommandRequest
    {
        public byte Command;
        public TaskCompletionSource<bool> Completion;
    }

    internal static class RemoteCommand
    {
        public const byte Play = 0;
        public const byte Pause = 1;
        public const byte TogglePlayPause = 2;
        public const byte NextTrack = 3;
        public const byte PreviousTrack = 4;
    }

    public sealed class AmsSource
    {
        private static readonly Guid serviceId = new Guid("89D3502B-0F36-433A-8EF4-C502AD55F8DC");
        private static readonly Guid remoteCommandId = new Guid("9B3C81D8-57B1-4A8A-B8DF-0E56F7CA51C2");
        private static readonly Guid entityId = new Guid("2F7CABCE-808D-411F-9A0C-BB92BA96C102");
        private static readonly Guid attributeId = new Guid("C6B2F38C-23AB-46D8-A6AB-A3A870BBD5D7");
        private readonly ConcurrentQueue<Packet> packets = new ConcurrentQueue<Packet>();
        private readonly ConcurrentQueue<RemoteCommandRequest> remoteCommands = new ConcurrentQueue<RemoteCommandRequest>();
        private readonly DeviceChoice choice;
        private readonly Action<SourceUpdate> publish;
        private volatile byte[] supportedRemoteCommands = new byte[0];
        private volatile bool remoteCommandWritable;
        private volatile bool receivedRemoteCommandList;
        private GattWriteOption remoteCommandWriteOption = GattWriteOption.WriteWithResponse;
        private long revision;
        private bool dirty = true;
        private bool waitingPublished;
        private bool snapshotReadStarted;
        private bool snapshotIssueLogged;
        private volatile bool connectionLost;
        private bool stableTitleFromArtist;
        private long metadataChangedAt;
        private Track stableTrack;
        private string stablePlayer;

        public AmsSource(DeviceChoice choice, Action<SourceUpdate> publish, long revision)
        {
            this.choice = choice;
            this.publish = publish;
            this.revision = revision;
        }

        public bool SupportsRemoteCommand(byte command)
        {
            return remoteCommandWritable && (!receivedRemoteCommandList || Array.IndexOf(supportedRemoteCommands, command) >= 0);
        }

        public Task SendRemoteCommandAsync(byte command)
        {
            if (!SupportsRemoteCommand(command)) throw new InvalidOperationException("当前 iPhone 播放器暂不支持此操作。");
            var request = new RemoteCommandRequest { Command = command, Completion = new TaskCompletionSource<bool>() };
            remoteCommands.Enqueue(request);
            return request.Completion.Task;
        }

        public static async Task<List<DeviceChoice>> DevicesAsync(CancellationToken cancellation)
        {
            var devices = await WinRt.Await<DeviceInformationCollection>(
                DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)), cancellation);
            return devices.Select(device => new DeviceChoice { Id = device.Id, Name = device.Name }).ToList();
        }

        public void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var tick = Stopwatch.GetTimestamp();
            byte[] bytes;
            using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
            {
                bytes = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(bytes);
            }
            if (bytes.Length < 3) return;
            packets.Enqueue(new Packet { Entity = bytes[0], Attribute = bytes[1], Truncated = (bytes[2] & 1) != 0,
                Value = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Timestamp = tick });
        }

        public void OnRemoteCommandsChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
            {
                var commands = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(commands);
                supportedRemoteCommands = commands;
                receivedRemoteCommandList = true;
                Console.WriteLine("AMS_REMOTE: supported=" + String.Join(",", commands.Select(command => command.ToString(CultureInfo.InvariantCulture)).ToArray()));
            }
        }

        public void OnConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) connectionLost = true;
        }

        private static byte[] Bytes(GattReadResult result)
        {
            using (var reader = DataReader.FromBuffer(result.Value))
            {
                var bytes = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(bytes);
                return bytes;
            }
        }

        private static async Task WriteAsync(GattCharacteristic characteristic, byte[] bytes, CancellationToken cancellation)
        {
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(bytes);
                var result = await WinRt.Await<GattWriteResult>(characteristic.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse), cancellation);
                if (result.Status != GattCommunicationStatus.Success)
                    throw new InvalidOperationException("手机属性配置失败：" + result.Status);
            }
        }

        private async Task ProcessRemoteCommandsAsync(GattCharacteristic characteristic, CancellationToken cancellation)
        {
            RemoteCommandRequest request;
            while (remoteCommands.TryDequeue(out request))
            {
                if (request.Completion.Task.IsCanceled) continue;
                try
                {
                    if (characteristic == null || !SupportsRemoteCommand(request.Command))
                        throw new InvalidOperationException("当前 iPhone 播放器暂不支持此操作。");
                    using (var writer = new DataWriter())
                    {
                        writer.WriteByte(request.Command);
                        var result = await WinRt.Await<GattWriteResult>(characteristic.WriteValueWithResultAsync(
                            writer.DetachBuffer(), remoteCommandWriteOption), cancellation);
                        if (result.Status != GattCommunicationStatus.Success)
                            throw new InvalidOperationException(result.ProtocolError == 0xA1
                                ? "当前播放器未接受此操作。" : "iPhone 未能执行此播放操作。");
                    }
                    request.Completion.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    request.Completion.TrySetCanceled();
                    throw;
                }
                catch (Exception error) { request.Completion.TrySetException(error); }
            }
        }

        private static async Task<string> ReadAsync(GattCharacteristic characteristic, byte entity, byte attribute, bool optional, CancellationToken cancellation)
        {
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(new[] { entity, attribute });
                var selection = await WinRt.Await<GattWriteResult>(characteristic.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse), cancellation);
                if (selection.ProtocolError == 0xA2)
                {
                    if (optional) return "";
                    throw new FormatException("手机当前未提供媒体属性：" + entity + "/" + attribute);
                }
                if (selection.Status != GattCommunicationStatus.Success)
                    throw new InvalidOperationException("手机属性选择失败：" + selection.Status + " / " + selection.ProtocolError);
            }
            var read = await WinRt.Await<GattReadResult>(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), cancellation);
            if (read.ProtocolError == 0xA2)
            {
                if (optional) return "";
                throw new FormatException("手机当前未提供媒体属性：" + entity + "/" + attribute);
            }
            if (read.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException("读取手机状态失败：" + read.Status + " / " + read.ProtocolError);
            return Encoding.UTF8.GetString(Bytes(read));
        }

        internal static double ParseDuration(string value)
        {
            if (value.Length == 0) return 0;
            double duration;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                || Double.IsNaN(duration) || Double.IsInfinity(duration) || duration <= 0)
                throw new FormatException("手机返回了无效的歌曲时长：" + value);
            return duration;
        }

        internal static bool IsPositionWithinDuration(double duration, double position)
        {
            return duration <= 0 || position <= duration + 0.5;
        }

        private async Task<SourceUpdate> SnapshotAsync(GattCharacteristic attribute, CancellationToken cancellation)
        {
            var title = await ReadAsync(attribute, 2, 2, false, cancellation);
            var artist = await ReadAsync(attribute, 2, 0, true, cancellation);
            var album = await ReadAsync(attribute, 2, 1, true, cancellation);
            var duration = await ReadAsync(attribute, 2, 3, true, cancellation);
            var player = await ReadAsync(attribute, 0, 0, false, cancellation);
            var info = await ReadAsync(attribute, 0, 1, false, cancellation);
            var playback = Playback.Parse(info, Stopwatch.GetTimestamp());
            var artistAfter = await ReadAsync(attribute, 2, 0, true, cancellation);
            var titleAfter = await ReadAsync(attribute, 2, 2, false, cancellation);
            var albumAfter = await ReadAsync(attribute, 2, 1, true, cancellation);
            var durationAfter = await ReadAsync(attribute, 2, 3, true, cancellation);
            var playerAfter = await ReadAsync(attribute, 0, 0, false, cancellation);
            var length = 0.0;
            var durationAvailable = AmsTrackMetadata.TryParseDuration(duration, out length);
            double lengthAfter;
            var durationAvailableAfter = AmsTrackMetadata.TryParseDuration(durationAfter, out lengthAfter);
            var artistStable = artist == artistAfter;
            var albumStable = album == albumAfter;
            var durationStable = durationAvailable == durationAvailableAfter
                && (!durationAvailable || Math.Abs(length - lengthAfter) <= 2);
            var playerStable = player == playerAfter;
            var rawTrack = new Track { Title = title, Artist = artist, Album = album, Duration = length };
            bool titleFromArtist;
            var track = AmsTrackMetadata.Normalize(player, rawTrack, out titleFromArtist);
            var titleStable = AmsTrackMetadata.IsTitleStable(titleFromArtist, title, titleAfter);
            var hasTitle = track.Title.Length > 0;
            var positionValid = IsPositionWithinDuration(length, playback.Seconds);
            if (!titleStable || !artistStable || !albumStable || !durationStable || !playerStable || !hasTitle || !positionValid)
                return null;
            return new SourceUpdate { Kind = UpdateKind.Snapshot, Revision = revision, Player = player,
                Track = track, Playback = playback, TitleFromArtist = titleFromArtist };
        }

        internal static bool ChangesPlayer(Packet packet, string player)
        {
            return packet.Entity == 0 && packet.Attribute == 0 && packet.Value != player;
        }

        private void Transition(long timestamp, string message)
        {
            metadataChangedAt = timestamp;
            if (!dirty)
            {
                dirty = true;
                waitingPublished = true;
                snapshotReadStarted = false;
                snapshotIssueLogged = false;
                revision++;
                publish(new SourceUpdate { Kind = UpdateKind.Transition, Revision = revision, Message = message });
            }
            else if (!waitingPublished)
            {
                waitingPublished = true;
                publish(new SourceUpdate { Kind = UpdateKind.Transition, Revision = revision, Message = message });
            }
        }

        public async Task RunAsync(CancellationToken cancellation)
        {
            BluetoothLEDevice device = null;
            GattDeviceService service = null;
            IDisposable values = null;
            IDisposable remoteValues = null;
            IDisposable connection = null;
            try
            {
                device = await WinRt.Await<BluetoothLEDevice>(BluetoothLEDevice.FromIdAsync(choice.Id), cancellation);
                if (device == null) throw new InvalidOperationException("无法访问这部 iPhone，请确认蓝牙连接。");
                var services = await WinRt.Await<GattDeviceServicesResult>(device.GetGattServicesForUuidAsync(serviceId, BluetoothCacheMode.Uncached), cancellation);
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count != 1)
                    throw new InvalidOperationException("未发现手机播放状态。请确认蓝牙连接并开始播放音乐。");
                service = services.Services[0];
                await WinRt.Await<GattOpenStatus>(service.OpenAsync(GattSharingMode.SharedReadAndWrite), cancellation);
                if (service.SharingMode != GattSharingMode.SharedReadAndWrite) throw new InvalidOperationException("手机播放状态访问被拒绝。");
                var characters = await WinRt.Await<GattCharacteristicsResult>(service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached), cancellation);
                if (characters.Status != GattCommunicationStatus.Success) throw new InvalidOperationException("无法读取手机播放属性。");
                var entity = characters.Characteristics.Single(character => character.Uuid == entityId);
                var attribute = characters.Characteristics.Single(character => character.Uuid == attributeId);
                var remote = characters.Characteristics.FirstOrDefault(character => character.Uuid == remoteCommandId);
                values = WinRt.Subscribe(entity, "ValueChanged", this, "OnValueChanged");
                connection = WinRt.Subscribe(device, "ConnectionStatusChanged", this, "OnConnectionChanged");
                var notify = await WinRt.Await<GattCommunicationStatus>(entity.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify), cancellation);
                if (notify != GattCommunicationStatus.Success) throw new InvalidOperationException("手机播放通知订阅失败。");
                if (remote != null)
                {
                    var properties = remote.CharacteristicProperties;
                    remoteCommandWritable = (properties & (GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse)) != 0;
                    Console.WriteLine("AMS_REMOTE: properties=" + properties);
                    remoteCommandWriteOption = (properties & GattCharacteristicProperties.Write) != 0
                        ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
                    var remoteNotify = (properties & GattCharacteristicProperties.Notify) != 0
                        ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                        : (properties & GattCharacteristicProperties.Indicate) != 0
                            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                            : GattClientCharacteristicConfigurationDescriptorValue.None;
                    if (remoteNotify != GattClientCharacteristicConfigurationDescriptorValue.None)
                    {
                        try
                        {
                            remoteValues = WinRt.Subscribe(remote, "ValueChanged", this, "OnRemoteCommandsChanged");
                            var commandNotify = await WinRt.Await<GattCommunicationStatus>(
                                remote.WriteClientCharacteristicConfigurationDescriptorAsync(remoteNotify), cancellation);
                            if (commandNotify != GattCommunicationStatus.Success)
                            {
                                remoteValues.Dispose();
                                remoteValues = null;
                                Console.WriteLine("AMS_REMOTE: command list unavailable; standard controls remain available");
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error)
                        {
                            if (remoteValues != null) { remoteValues.Dispose(); remoteValues = null; }
                            Console.WriteLine("AMS_REMOTE: command list unavailable: " + error.Message);
                        }
                    }
                    else Console.WriteLine("AMS_REMOTE: command list is not notifiable");
                    if (!remoteCommandWritable) Console.WriteLine("AMS_REMOTE: playback commands are not writable");
                }
                else Console.WriteLine("AMS_REMOTE: iPhone does not expose playback commands");
                await WriteAsync(entity, new byte[] { 0, 0, 1 }, cancellation);
                await WriteAsync(entity, new byte[] { 2, 0, 1, 2, 3 }, cancellation);
                var healthAt = Stopwatch.GetTimestamp();

                while (true)
                {
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (connectionLost || device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                            throw new InvalidOperationException("手机连接已断开。歌词已停止，连接后可重新同步。");
                        await ProcessRemoteCommandsAsync(remote, cancellation);
                        Packet packet;
                        while (packets.TryDequeue(out packet))
                        {
                            if (packet.Truncated) packet.Value = await ReadAsync(attribute, packet.Entity, packet.Attribute, false, cancellation);
                            var trackChanged = packet.Entity == 2 && !dirty
                                && AmsTrackMetadata.HasTrackChanged(packet, stableTrack, stablePlayer, stableTitleFromArtist);
                            if (packet.Entity == 2 && (dirty || trackChanged))
                                Transition(packet.Timestamp, "正在切换歌曲");
                            else if (ChangesPlayer(packet, stablePlayer))
                                Transition(packet.Timestamp, "正在切换播放器");
                            else if (packet.Entity == 0 && packet.Attribute == 1 && !dirty)
                                publish(new SourceUpdate { Kind = UpdateKind.Playback, Revision = revision, Playback = Playback.Parse(packet.Value, packet.Timestamp) });
                        }
                        var now = Stopwatch.GetTimestamp();
                        if (dirty && (now - metadataChangedAt) / (double)Stopwatch.Frequency >= 1)
                        {
                            if (!snapshotReadStarted)
                            {
                                Console.WriteLine("AMS: Reading current media snapshot");
                                snapshotReadStarted = true;
                            }
                            var snapshot = await SnapshotAsync(attribute, cancellation);
                            if (snapshot == null && !snapshotIssueLogged)
                            {
                                Console.WriteLine("AMS: Media metadata changed while being read; retrying");
                                snapshotIssueLogged = true;
                            }
                            var conflicts = false;
                            while (packets.TryDequeue(out packet))
                            {
                                if (packet.Truncated) packet.Value = await ReadAsync(attribute, packet.Entity, packet.Attribute, false, cancellation);
                                if (snapshot != null && AmsTrackMetadata.HasTrackChanged(packet, snapshot.Track,
                                    snapshot.Player, snapshot.TitleFromArtist))
                                    conflicts = true;
                                if (snapshot != null && ChangesPlayer(packet, snapshot.Player))
                                    conflicts = true;
                                if (snapshot != null && packet.Entity == 0 && packet.Attribute == 1)
                                {
                                    var latest = Playback.Parse(packet.Value, packet.Timestamp);
                                    if (latest.Timestamp > snapshot.Playback.Timestamp) snapshot.Playback = latest;
                                    if (snapshot.Track.Duration > 0 && latest.Seconds > snapshot.Track.Duration + 0.5) conflicts = true;
                                }
                            }
                            if (snapshot != null && !conflicts)
                            {
                                stableTrack = snapshot.Track;
                                stablePlayer = snapshot.Player;
                                stableTitleFromArtist = snapshot.TitleFromArtist;
                                dirty = false;
                                waitingPublished = false;
                                snapshotReadStarted = false;
                                snapshotIssueLogged = false;
                                publish(snapshot);
                            }
                            else metadataChangedAt = Stopwatch.GetTimestamp();
                        }
                        if (!dirty && (now - healthAt) / (double)Stopwatch.Frequency >= 15)
                        {
                            healthAt = now;
                            var health = await SnapshotAsync(attribute, cancellation);
                            if (health != null && health.Player != stablePlayer)
                                Transition(now, "正在切换播放器");
                            else if (health != null && AmsTrackMetadata.HasTrackChanged(health.Track, health.TitleFromArtist,
                                health.Player, stableTrack, stableTitleFromArtist, stablePlayer))
                                Transition(now, "正在切换歌曲");
                            else if (health != null)
                            {
                                publish(new SourceUpdate { Kind = UpdateKind.Playback, Revision = revision, Playback = health.Playback });
                            }
                        }
                    }
                    catch (FormatException error)
                    {
                        if (!snapshotIssueLogged)
                        {
                            Console.WriteLine("AMS_INVALID_STATE: " + error.Message);
                            snapshotIssueLogged = true;
                        }
                        if (dirty || stableTrack == null)
                            Transition(Stopwatch.GetTimestamp(), "等待手机提供有效进度");
                    }
                    await Task.Delay(80, cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Console.WriteLine("AMS_ERROR: " + error.ToString());
                publish(new SourceUpdate { Kind = UpdateKind.Disconnected, Revision = revision + 1, Message = error.Message });
            }
            finally
            {
                remoteCommandWritable = false;
                supportedRemoteCommands = new byte[0];
                receivedRemoteCommandList = false;
                if (remoteValues != null) remoteValues.Dispose();
                if (values != null) values.Dispose();
                if (connection != null) connection.Dispose();
                if (service != null) service.Dispose();
                if (device != null) device.Dispose();
                RemoteCommandRequest request;
                while (remoteCommands.TryDequeue(out request))
                    request.Completion.TrySetException(new InvalidOperationException("iPhone 连接已结束，未能发送播放操作。"));
            }
        }
    }
}
