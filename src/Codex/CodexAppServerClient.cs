using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexUsageTray
{
    internal sealed class CodexAppServerClient : IDisposable
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly SemaphoreSlim _startGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private Process _process;
        private StreamWriter _writer;
        private Task _stdoutTask;
        private Task _stderrTask;
        private int _nextRequestId;
        private volatile bool _initialized;
        private volatile bool _disposed;

        public event EventHandler RateLimitsChanged;

        public bool IsRunning
        {
            get
            {
                try
                {
                    return !_disposed && _initialized && _process != null && !_process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<RateLimitSnapshot> ReadRateLimitsAsync()
        {
            await EnsureStartedAsync();
            Dictionary<string, object> result = await RequestCoreAsync("account/rateLimits/read", null);
            return RateLimitSnapshot.FromResult(result);
        }

        public async Task<AccountSnapshot> ReadAccountAsync()
        {
            await EnsureStartedAsync();
            Dictionary<string, object> parameters = new Dictionary<string, object>
            {
                { "refreshToken", false }
            };
            Dictionary<string, object> result = await RequestCoreAsync("account/read", parameters);
            return AccountSnapshot.FromResult(result);
        }

        public async Task<TokenUsageSnapshot> ReadTokenUsageAsync()
        {
            await EnsureStartedAsync();
            Dictionary<string, object> result = await RequestCoreAsync("account/usage/read", null);
            return TokenUsageSnapshot.FromResult(result);
        }

        private async Task EnsureStartedAsync()
        {
            if (IsRunning)
            {
                return;
            }

            await _startGate.WaitAsync();
            try
            {
                if (IsRunning)
                {
                    return;
                }

                if (_disposed)
                {
                    throw new ObjectDisposedException("CodexAppServerClient");
                }

                StopProcess();
                string executable = CodexExecutableLocator.Find();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "app-server --stdio",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.Exited += delegate { FailAllPending(new IOException("Codex App Server가 종료되었습니다.")); };
                if (!_process.Start())
                {
                    throw new InvalidOperationException("Codex App Server를 시작하지 못했습니다.");
                }

                _writer = _process.StandardInput;
                _writer.AutoFlush = true;
                _stdoutTask = Task.Run(() => ReadLoopAsync(_process.StandardOutput));
                _stderrTask = Task.Run(() => DrainErrorAsync(_process.StandardError));

                var initializeParams = new Dictionary<string, object>
                {
                    {
                        "clientInfo", new Dictionary<string, object>
                        {
                            { "name", "codex_usage_tray" },
                            { "title", "Codex Usage Tray" },
                            { "version", "1.6.0" }
                        }
                    }
                };

                await RequestCoreAsync("initialize", initializeParams);
                await SendNotificationAsync("initialized", new Dictionary<string, object>());
                _initialized = true;
            }
            catch
            {
                StopProcess();
                throw;
            }
            finally
            {
                _startGate.Release();
            }
        }

        private async Task<Dictionary<string, object>> RequestCoreAsync(string method, object parameters)
        {
            int id = Interlocked.Increment(ref _nextRequestId);
            TaskCompletionSource<Dictionary<string, object>> completion =
                new TaskCompletionSource<Dictionary<string, object>>();
            if (!_pending.TryAdd(id, completion))
            {
                throw new InvalidOperationException("요청 ID를 등록하지 못했습니다.");
            }

            try
            {
                Dictionary<string, object> message = new Dictionary<string, object>
                {
                    { "method", method },
                    { "id", id }
                };
                if (parameters != null)
                {
                    message["params"] = parameters;
                }

                await SendRawAsync(message);
                Task finished = await Task.WhenAny(completion.Task, Task.Delay(15000));
                if (finished != completion.Task)
                {
                    throw new TimeoutException("Codex App Server 응답 시간이 초과되었습니다.");
                }

                return await completion.Task;
            }
            finally
            {
                TaskCompletionSource<Dictionary<string, object>> ignored;
                _pending.TryRemove(id, out ignored);
            }
        }

        private Task SendNotificationAsync(string method, object parameters)
        {
            Dictionary<string, object> message = new Dictionary<string, object>
            {
                { "method", method },
                { "params", parameters }
            };
            return SendRawAsync(message);
        }

        private async Task SendRawAsync(object message)
        {
            if (_writer == null)
            {
                throw new IOException("Codex App Server 연결이 열려 있지 않습니다.");
            }

            string line = _json.Serialize(message);
            await _writeGate.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(line);
                await _writer.FlushAsync();
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private async Task ReadLoopAsync(StreamReader reader)
        {
            try
            {
                string line;
                while (!_disposed && (line = await reader.ReadLineAsync()) != null)
                {
                    HandleMessage(line);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    FailAllPending(ex);
                }
            }
        }

        private static async Task DrainErrorAsync(StreamReader reader)
        {
            try
            {
                while (await reader.ReadLineAsync() != null)
                {
                    // Intentionally discarded: diagnostics can contain local paths.
                }
            }
            catch
            {
                // The process is closing.
            }
        }

        private void HandleMessage(string line)
        {
            Dictionary<string, object> message;
            try
            {
                message = _json.DeserializeObject(line) as Dictionary<string, object>;
            }
            catch
            {
                return;
            }

            if (message == null)
            {
                return;
            }

            object idValue;
            if (message.TryGetValue("id", out idValue) && idValue != null)
            {
                int id;
                try
                {
                    id = Convert.ToInt32(idValue, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return;
                }

                TaskCompletionSource<Dictionary<string, object>> completion;
                if (!_pending.TryGetValue(id, out completion))
                {
                    return;
                }

                object errorValue;
                if (message.TryGetValue("error", out errorValue) && errorValue != null)
                {
                    Dictionary<string, object> error = errorValue as Dictionary<string, object>;
                    string errorMessage = error != null && error.ContainsKey("message")
                        ? Convert.ToString(error["message"], CultureInfo.InvariantCulture)
                        : "Codex App Server 요청이 실패했습니다.";
                    completion.TrySetException(new InvalidOperationException(errorMessage));
                    return;
                }

                object resultValue;
                Dictionary<string, object> result = message.TryGetValue("result", out resultValue)
                    ? resultValue as Dictionary<string, object>
                    : null;
                completion.TrySetResult(result ?? new Dictionary<string, object>());
                return;
            }

            object methodValue;
            if (message.TryGetValue("method", out methodValue) &&
                string.Equals(Convert.ToString(methodValue, CultureInfo.InvariantCulture),
                    "account/rateLimits/updated", StringComparison.Ordinal))
            {
                EventHandler handler = RateLimitsChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void FailAllPending(Exception error)
        {
            foreach (KeyValuePair<int, TaskCompletionSource<Dictionary<string, object>>> entry in _pending)
            {
                entry.Value.TrySetException(error);
            }
        }

        private void StopProcess()
        {
            _initialized = false;
            _stdoutTask = null;
            _stderrTask = null;
            StreamWriter writer = _writer;
            _writer = null;

            if (writer != null)
            {
                try
                {
                    writer.Close();
                }
                catch
                {
                    // Best-effort shutdown.
                }
            }

            Process process = _process;
            _process = null;
            if (process != null)
            {
                try
                {
                    if (!process.HasExited && !process.WaitForExit(1200))
                    {
                        process.Kill();
                        process.WaitForExit(1200);
                    }
                }
                catch
                {
                    // Best-effort shutdown.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FailAllPending(new ObjectDisposedException("CodexAppServerClient"));
            StopProcess();
            _startGate.Dispose();
            _writeGate.Dispose();
        }
    }

}
