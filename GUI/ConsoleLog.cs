using System;
using System.IO;
using System.Text;

namespace GUI
{
    public sealed class ConsoleLog
    {
        private const int MaxChars = 400_000;

        public static ConsoleLog Instance { get; } = new();

        private readonly object gate = new();
        private readonly StringBuilder history = new();
        private readonly StringBuilder pending = new();

        public event Action? Cleared;

        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            lock (gate)
            {
                history.Append(text);
                pending.Append(text);

                if (history.Length > MaxChars)
                {
                    int cut = history.Length - MaxChars;
                    // Drop to the end of the line so the view never starts mid sentence
                    for (int i = cut; i < history.Length && i < cut + 500; i++)
                    {
                        if (history[i] == '\n') { cut = i + 1; break; }
                    }
                    history.Remove(0, cut);
                }
            }
        }

        /// Everything written since the last drain
        public string DrainPending()
        {
            lock (gate)
            {
                if (pending.Length == 0) return string.Empty;
                string text = pending.ToString();
                pending.Clear();
                return text;
            }
        }

        public string TakeSnapshot()
        {
            lock (gate)
            {
                pending.Clear();
                return history.ToString();
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                history.Clear();
                pending.Clear();
            }
            Cleared?.Invoke();
        }

        public static void Install()
        {
            var writer = new LogWriter(Instance);
            Console.SetOut(writer);
            Console.SetError(writer);
        }

        private sealed class LogWriter : TextWriter
        {
            private readonly ConsoleLog log;

            public LogWriter(ConsoleLog log) => this.log = log;

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value) => log.Write(value.ToString());

            public override void Write(string? value) => log.Write(value ?? "");

            public override void WriteLine() => log.Write(NewLine);

            public override void WriteLine(string? value) => log.Write((value ?? "") + NewLine);
        }
    }
}
