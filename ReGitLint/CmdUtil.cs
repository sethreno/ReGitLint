using System;
using System.Diagnostics;
using System.Threading;

namespace ReGitLint {
    public static class CmdUtil {
        /// <summary>
        /// Runs the specified command and returns it's exit code.
        /// </summary>
        /// <param name="cmd">the cmd to run</param>
        /// <param name="args">args to pass the cmd</param>
        /// <param name="outputCallback">
        /// Optional callback for std out. Defaults to Console.WriteLine
        /// </param>
        /// <param name="errorCallback">
        /// Optional callback for error out. Defaults to Console.WriteLine
        /// </param>
        /// <param name="cmdTimeout">
        /// Optional timeout for the cmd. Defaults to 10 minutes.
        /// </param>
        /// <param name="outputTimeout">
        /// Optional timeout for the output. Defaults to 10 minutes.
        /// </param>
        /// <returns>The exit code of the cmd</returns>
        public static int Run(
            string cmd,
            string args,
            Action<string> outputCallback = null,
            Action<string> errorCallback = null,
            TimeSpan? cmdTimeout = null,
            TimeSpan? outputTimeout = null
        ) {
            void WriteToConsole(string data) {
                Console.WriteLine(data);
            }

            outputCallback = outputCallback ?? WriteToConsole;
            errorCallback = errorCallback ?? WriteToConsole;
            cmdTimeout = cmdTimeout ?? TimeSpan.FromMinutes(10);
            outputTimeout = outputTimeout ?? TimeSpan.FromMinutes(10);

            using (var process = new Process()) {
                process.StartInfo.FileName = cmd;
                process.StartInfo.Arguments = args;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                using (var outputWaitHandle = new AutoResetEvent(false))
                using (var errorWaitHandle = new AutoResetEvent(false)) {
                    process.OutputDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            outputWaitHandle.Set();
                        } else {
                            outputCallback(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (sender, e) => {
                        if (e.Data == null) {
                            errorWaitHandle.Set();
                        } else {
                            errorCallback(e.Data);
                        }
                    };

                    process.Start();

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var cmdTimeoutMs = cmdTimeout.Value.TotalMilliseconds;
                    var outTimeoutMs = outputTimeout.Value.TotalMilliseconds;

                    if (process.WaitForExit((int)cmdTimeoutMs) &&
                        outputWaitHandle.WaitOne((int)outTimeoutMs) &&
                        errorWaitHandle.WaitOne((int)outTimeoutMs)) {
                        return process.ExitCode;
                    }

                    Console.WriteLine($"{cmd} timed out");
                    return 1;
                }
            }
        }
    }
}
