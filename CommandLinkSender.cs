// ================================================================
//  CommandLinkSender.cs  –  GCS_240626
//
//  Sends GCS command packets to sgsim at 10 Hz (every 100 ms) over
//  the shared COM9 / COM24 virtual serial link.
//
//  Usage:
//    _cmdSender = new CommandLinkSender(_receiver);
//    _cmdSender.State.ApplyHilsDefaults();
//    _cmdSender.Start();
//    ...
//    _cmdSender.State.AirModesEnabledSwt = true;   // no restart needed
//    ...
//    _cmdSender.Dispose();
// ================================================================

using System;
using System.Threading;

namespace GCS_240626
{
    public sealed class CommandLinkSender : IDisposable
    {
        private const int INTERVAL_MS = 100;    // 10 Hz

        private readonly PacketReceiver _receiver;
        private Timer   _timer;
        private int     _running;       // 0 = stopped, 1 = running (Interlocked)
        private int     _txCount;       // packets sent this second
        private int     _txRate;        // packets/s snapshot (last full second)
        private DateTime _rateStart = DateTime.UtcNow;

        /// <summary>Live command state — mutate any field between sends; thread-safe reads only.</summary>
        public CommandState State { get; } = new CommandState();

        /// <summary>Packets actually transmitted per second (updated every second).</summary>
        public int TxRatePps => _txRate;

        /// <summary>True while periodic transmission is active.</summary>
        public bool IsRunning => _running == 1;

        public CommandLinkSender(PacketReceiver receiver)
        {
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        }

        /// <summary>Start periodic transmission.</summary>
        public void Start()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) == 0)
            {
                _rateStart = DateTime.UtcNow;
                _txCount   = 0;
                _timer = new Timer(_ => Tick(), null, 0, INTERVAL_MS);
            }
        }

        /// <summary>Stop periodic transmission (does not dispose).</summary>
        public void Stop()
        {
            if (Interlocked.CompareExchange(ref _running, 0, 1) == 1)
            {
                _timer?.Dispose();
                _timer  = null;
                _txRate = 0;
            }
        }

        private void Tick()
        {
            if (_running == 0) return;
            try
            {
                byte[] pkt = CommandPacketEncoder.Encode(State);
                _receiver.SendRaw(pkt);

                // Update TX rate counter
                _txCount++;
                var elapsed = DateTime.UtcNow - _rateStart;
                if (elapsed.TotalSeconds >= 1.0)
                {
                    _txRate    = (int)Math.Round(_txCount / elapsed.TotalSeconds);
                    _txCount   = 0;
                    _rateStart = DateTime.UtcNow;
                }
            }
            catch
            {
                // Swallow serial errors — port may not be open yet;
                // the next tick will retry automatically.
            }
        }

        /// <summary>
        /// Immediately encode and transmit one packet outside the periodic tick.
        /// Safe to call at any time; swallows errors if the port is not open.
        /// </summary>
        public void SendNow()
        {
            try
            {
                byte[] pkt = CommandPacketEncoder.Encode(State);
                _receiver.SendRaw(pkt);
            }
            catch { }
        }

        public void Dispose() => Stop();
    }
}
