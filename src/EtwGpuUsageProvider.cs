using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace WinOptimizer.AI
{
    public sealed class EtwGpuUsageProvider : IDisposable
    {
        private readonly ConcurrentDictionary<int, double> _pidToGpu = new();
        private TraceEventSession _session;
        private Task _readerTask;
        private CancellationTokenSource _cts;

        public event Action<string> OnLog;

        public bool IsRunning => _session != null;

        public IReadOnlyDictionary<int, double> Snapshot()
        {
            return new Dictionary<int, double>(_pidToGpu);
        }

        public bool TryStart()
        {
            try
            {
                if (_session != null)
                    return true;

                // Requires admin.
                _cts = new CancellationTokenSource();

                // Use a unique session name per process.
                var name = "WinOptimizer.AI.GpuEtw." + Environment.ProcessId;
                _session = new TraceEventSession(name);
                _session.StopOnDispose = true;

                // DxgKrnl is the main graphics kernel provider. 
                // We need specific keywords to get GPU utilization events.
                // 0x1 = Base, 0x2 = Memory, 0x4 = Scheduling, 0x8 = Context, 0x10 = Performance
                _session.EnableProvider("Microsoft-Windows-DxgKrnl", TraceEventLevel.Informational, 
                    0x1 | 0x2 | 0x4 | 0x8 | 0x10);  // Enable performance and context keywords
                
                // Also enable DXGI for DirectX apps
                _session.EnableProvider("Microsoft-Windows-DXGI", TraceEventLevel.Informational, ulong.MaxValue);
                
                // And DWM for Desktop Window Manager GPU usage
                _session.EnableProvider("Microsoft-Windows-Dwm-Dwm", TraceEventLevel.Informational, ulong.MaxValue);

                var source = _session.Source;
                source.Dynamic.All += OnDynamicEvent;

                _readerTask = Task.Run(() =>
                {
                    try
                    {
                        source.Process();
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke("ETW GPU source error: " + ex.Message);
                    }
                }, _cts.Token);

                OnLog?.Invoke("ETW GPU provider started (DxgKrnl).");
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke("Failed to start ETW GPU provider: " + ex.Message);
                Stop();
                return false;
            }
        }

        private int _eventCount = 0;
        private DateTime _lastLogTime = DateTime.MinValue;

        private void OnDynamicEvent(TraceEvent e)
        {
            try
            {
                _eventCount++;
                
                // Log every 5 seconds to avoid spam
                var now = DateTime.Now;
                if ((now - _lastLogTime).TotalSeconds >= 5)
                {
                    OnLog?.Invoke($"[DIAG-ETW] Received {_eventCount} events so far");
                    _lastLogTime = now;
                }

                // We try multiple common payload names.
                int pid = 0;
                double util = -1;

                pid = ReadIntPayload(e, new[] { "ProcessId", "PID", "ClientProcessId" });
                util = ReadDoublePayload(e, new[] { "Utilization", "UtilizationPercentage", "GpuUtilization", "ContextUtilization" });

                if (pid > 0 && util >= 0)
                {
                    // Clamp 0..100
                    util = Math.Max(0, Math.Min(100, util));
                    _pidToGpu[pid] = util;
                    OnLog?.Invoke($"[DIAG-ETW] GPU event: PID={pid}, Util={util:F2}%");
                }
                else if (pid > 0)
                {
                    // Found PID but no utilization - log payload names for debugging
                    var payloadNames = string.Join(", ", e.PayloadNames ?? new string[0]);
                    OnLog?.Invoke($"[DIAG-ETW] Event with PID={pid} but no utilization. Payloads: {payloadNames}");
                }
            }
            catch
            {
            }
        }

        private int ReadIntPayload(TraceEvent e, string[] names)
        {
            foreach (var n in names)
            {
                var idx = FindPayloadIndex(e, n);
                if (idx >= 0)
                {
                    var v = e.PayloadValue(idx);
                    if (v is int i) return i;
                    if (v is long l) return (int)l;
                    if (v != null && int.TryParse(v.ToString(), out var parsed)) return parsed;
                }
            }
            return 0;
        }

        private double ReadDoublePayload(TraceEvent e, string[] names)
        {
            foreach (var n in names)
            {
                var idx = FindPayloadIndex(e, n);
                if (idx >= 0)
                {
                    var v = e.PayloadValue(idx);
                    if (v is float f) return f;
                    if (v is double d) return d;
                    if (v is int i) return i;
                    if (v is long l) return l;
                    if (v != null && double.TryParse(v.ToString(), out var parsed)) return parsed;
                }
            }
            return -1;
        }

        private int FindPayloadIndex(TraceEvent e, string name)
        {
            try
            {
                if (e?.PayloadNames == null)
                    return -1;

                var i = 0;
                foreach (var n in e.PayloadNames)
                {
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                        return i;
                    i++;
                }
            }
            catch
            {
            }

            return -1;
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _session?.Dispose();
            }
            catch
            {
            }

            _session = null;
            _cts = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
