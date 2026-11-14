using System.ComponentModel;
using System.Windows;

namespace Display_UpperMachine.Models
{
    /// <summary>
    /// 表格中的一行参数（点位）。
    /// 前 10 个只读（从从站读取），后 10 个可写（发送到从站）。
    /// 实现 INotifyPropertyChanged，便于实时刷新时界面自动更新。
    /// </summary>
    public class TagRow : INotifyPropertyChanged
    {
        /// <summary>参数名称。</summary>
        public string Name { get; set; } = "";

        /// <summary>起始寄存器地址（一个 float 占 2 个寄存器）。</summary>
        public int Address { get; set; }

        /// <summary>是否可写（true = 后 10 个可发送到从站的参数）。</summary>
        public bool CanWrite { get; set; }

        // 便于 XAML 直接绑定，省去值转换器
        /// <summary>是否只读（可写的取反）。</summary>
        public bool IsReadOnly => !CanWrite;

        /// <summary>写入按钮的可见性（仅可写参数显示）。</summary>
        public Visibility WriteButtonVisibility => CanWrite ? Visibility.Visible : Visibility.Collapsed;

        private float _value;
        /// <summary>参数当前值。</summary>
        public float Value
        {
            get => _value;
            set
            {
                if (_value.Equals(value)) return;
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        private string _rawHex = "";
        /// <summary>该参数对应的 2 个原始寄存器（十六进制，便于排查字节序）。</summary>
        public string RawHex
        {
            get => _rawHex;
            set
            {
                if (_rawHex == value) return;
                _rawHex = value;
                OnPropertyChanged(nameof(RawHex));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
