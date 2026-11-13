using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SendNotes
{
    /// <summary>
    /// The "Sent to PDT15 on 2026-11-03." comment added to a note once it has been sent. It is
    /// written in the user's interface language and recognised in any of the three.
    /// </summary>
    public static class SentMarker
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*(?:Sent to|Enviada a|Envoy\u00E9e \u00E0)\s+(\S+)\s+(?:on|el|le)\s+(\d{4}-\d{2}-\d{2})\s*\.?\s*$",
            RegexOptions.CultureInvariant);

        public static string Make(Strings s, string target, DateTime date)
        {
            return string.Format(s.SentMarker, target, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        public static bool TryParse(string text, out string target, out string date)
        {
            target = null;
            date = null;
            if (string.IsNullOrEmpty(text)) return false;
            Match m = Pattern.Match(text);
            if (!m.Success) return false;
            target = m.Groups[1].Value;
            date = m.Groups[2].Value;
            return true;
        }

        public static bool IsMarker(NoteComment c)
        {
            string target, date;
            return TryParse(c.Text, out target, out date);
        }

        /// <summary>Where the note was sent: "PDT15 2026-11-03" for each marker, in order.</summary>
        public static List<KeyValuePair<string, string>> SentTo(NoteThread thread)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (NoteComment c in thread.Comments)
            {
                string target, date;
                if (TryParse(c.Text, out target, out date))
                    list.Add(new KeyValuePair<string, string>(target, date));
            }
            return list;
        }

        public static bool WasSentTo(NoteThread thread, string target)
        {
            return SentTo(thread).Any(kv => string.Equals(kv.Key, target, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The comment offered at first: the last one with text that is not a "Sent to" marker (-1 when none).</summary>
        public static int DefaultComment(NoteThread thread)
        {
            for (int i = thread.Comments.Count - 1; i >= 0; i--)
                if (CanSend(thread.Comments[i])) return i;
            return -1;
        }

        public static bool CanSend(NoteComment c)
        {
            return c.HasText && !IsMarker(c);
        }
    }

    /// <summary>One note to send, with the comment chosen.</summary>
    public sealed class SendItem
    {
        public NoteThread Thread;
        public NoteComment Comment;
    }

    public sealed class SendReport
    {
        /// <summary>Source reference and the reference used in the target.</summary>
        public readonly List<KeyValuePair<string, string>> Sent = new List<KeyValuePair<string, string>>();
        public readonly List<string> Failed = new List<string>();
        public readonly List<string> MarkFailed = new List<string>();
        /// <summary>The target's notes could not be locked: nothing was sent.</summary>
        public bool NoTargetLock;
        /// <summary>The source's notes could not be locked: the notes were sent without a "Sent to" marker.</summary>
        public bool NoSourceLock;
    }

    /// <summary>
    /// Sends notes: one lock on the target's notes for the whole batch, a new note for each item,
    /// then one lock on the source's notes to add "Sent to ..." to each note that was sent.
    /// </summary>
    public static class SendRunner
    {
        public static SendReport Run(INoteProject source, INoteProject target, IList<SendItem> items, Strings s, DateTime today)
        {
            var report = new SendReport();
            var sent = new List<SendItem>();
            using (INoteWriter writer = target.OpenWriter())
            {
                if (writer == null)
                {
                    report.NoTargetLock = true;
                    return report;
                }
                foreach (SendItem item in items)
                {
                    try
                    {
                        string where = writer.AddNote(item.Thread, item.Comment);
                        report.Sent.Add(new KeyValuePair<string, string>(item.Thread.RangeReference, where));
                        sent.Add(item);
                    }
                    catch (Exception ex)
                    {
                        report.Failed.Add(string.Format(s.ResFailed, item.Thread.RangeReference, Message(ex)));
                    }
                }
                if (sent.Count > 0) writer.SendNotifications();
            }
            if (sent.Count == 0) return report;

            string marker = SentMarker.Make(s, target.ShortName, today);
            using (INoteWriter writer = source.OpenWriter())
            {
                if (writer == null)
                {
                    report.NoSourceLock = true;
                    return report;
                }
                foreach (SendItem item in sent)
                {
                    try
                    {
                        writer.AddComment(item.Thread, marker);
                    }
                    catch (Exception ex)
                    {
                        report.MarkFailed.Add(string.Format(s.ResMarkFailed, item.Thread.RangeReference, Message(ex)));
                    }
                }
                writer.SendNotifications();
            }
            return report;
        }

        /// <summary>The report as the window shows it.</summary>
        public static string Describe(SendReport r, INoteProject source, INoteProject target, Strings s)
        {
            var lines = new List<string>();
            if (r.NoTargetLock)
            {
                lines.Add(string.Format(s.ResNoTargetLock, target.ShortName));
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            lines.Add(string.Format(s.ResSent, r.Sent.Count, target.ShortName));
            foreach (var kv in r.Sent)
                lines.Add(string.Format(s.ResSentLine, kv.Key, target.ShortName, kv.Value));
            lines.AddRange(r.Failed);
            if (r.NoSourceLock) lines.Add(string.Format(s.ResNoSourceLock, source.ShortName));
            lines.AddRange(r.MarkFailed);
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string Message(Exception ex)
        {
            while (ex.InnerException != null && ex.InnerException.Message.Length > 0) ex = ex.InnerException;
            return ex.Message;
        }
    }
}
