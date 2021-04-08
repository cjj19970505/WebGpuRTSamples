using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ImageBlur
{
    public class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;


        void SetProperty<T>(ref T backendVar, T value, [CallerMemberName] string callerName = "")
        {
            backendVar = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(callerName));
        }

        int _FilterSize = 15;
        public int FilterSize
        {
            get
            {
                return _FilterSize;
            }
            set
            {
                SetProperty(ref _FilterSize, value);
            }
        }

        int _Iterations = 2;
        public int Iterations
        {
            get
            {
                return _Iterations;
            }
            set
            {
                SetProperty(ref _Iterations, value);
            }
        }
    }
}
