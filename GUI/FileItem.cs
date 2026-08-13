using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Core;

namespace GUI
{
    public class FileItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly Dictionary<string, FileItem> childIndex = new(StringComparer.Ordinal);

        public string Name { get; set; }

        public bool IsFile { get; }
        public string Type => IsFile ? "File" : "Folder";
        public ObservableCollection<FileItem> Children { get; set; } = new();

        private long _sizeInBytes;
        public long SizeInBytes
        {
            get => _sizeInBytes;
            set
            {
                if (_sizeInBytes == value) return;
                _sizeInBytes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeInBytes)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
            }
        }

        public string Size => Utils.FormatSize(SizeInBytes);
        public string Icon => IsFile ? "pack://application:,,,/icons/file2.png" : "pack://application:,,,/icons/folder.png";
        public SophonManifestAssetProperty? SourceFile { get; set; }
        public string Elements => IsFile || ElementsCount == 0 ? "" : $"{ElementsCount:# ##0} files";

        private long _elementsCount;
        public long ElementsCount
        {
            get => _elementsCount;
            set
            {
                if (_elementsCount == value) return;
                _elementsCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElementsCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elements)));
            }
        }

        public FileItem? Parent { get; set; }

        public FileItem? FindChild(string name) => childIndex.TryGetValue(name, out var child) ? child : null;

        public void AddChild(FileItem child)
        {
            Children.Add(child);
            childIndex[child.Name] = child;
        }

        public FileItem GetOrAddFolder(string name)
        {
            if (childIndex.TryGetValue(name, out var existing))
                return existing;

            var folder = new FileItem(name, 0, this, isFile: false);
            AddChild(folder);
            return folder;
        }

        public void ClearChildren()
        {
            Children.Clear();
            childIndex.Clear();
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public FileItem(string name, long sizeInBytes, FileItem? parent = null, SophonManifestAssetProperty? sourceFile = null, bool isFile = false)
        {
            Name = name;
            _sizeInBytes = sizeInBytes;
            Parent = parent;
            SourceFile = sourceFile;
            IsFile = isFile || sourceFile != null;
            _elementsCount = 0;
        }
    }
}
