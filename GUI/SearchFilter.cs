using System;
using System.Collections.Generic;
using Core;

namespace GUI
{
    public enum SearchItemType
    {
        All,
        FilesOnly,
        FoldersOnly
    }

    public sealed class SearchFilter
    {
        public SearchItemType Type { get; set; } = SearchItemType.All;
        public long? MinSize { get; set; }
        public long? MaxSize { get; set; }

        public bool IsActive => Type != SearchItemType.All || MinSize.HasValue || MaxSize.HasValue;

        public SearchFilter Clone() => new()
        {
            Type = Type,
            MinSize = MinSize,
            MaxSize = MaxSize
        };

        public bool AllowsFiles => Type != SearchItemType.FoldersOnly;
        public bool AllowsFolders => Type != SearchItemType.FilesOnly;

        public bool SizeInRange(long size)
        {
            if (MinSize.HasValue && size < MinSize.Value) return false;
            if (MaxSize.HasValue && size > MaxSize.Value) return false;
            return true;
        }

        public string Describe()
        {
            if (!IsActive) return "No filter set";

            var parts = new List<string>();
            if (Type == SearchItemType.FilesOnly) parts.Add("files only");
            else if (Type == SearchItemType.FoldersOnly) parts.Add("folders only");

            if (MinSize.HasValue && MaxSize.HasValue)
                parts.Add($"between {Utils.FormatSize(MinSize.Value)} and {Utils.FormatSize(MaxSize.Value)}");
            else if (MinSize.HasValue)
                parts.Add($"at least {Utils.FormatSize(MinSize.Value)}");
            else if (MaxSize.HasValue)
                parts.Add($"at most {Utils.FormatSize(MaxSize.Value)}");

            return string.Join(", ", parts);
        }
    }
}
