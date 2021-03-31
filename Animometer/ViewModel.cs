using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Animometer
{
    class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        int _NumTriangles = 2000;
        public int NumTriangles
        {
            get
            {
                return _NumTriangles;
            }
            set
            {
                SetProperty(ref _NumTriangles, value);
            }
        }

        bool _RenderBundles = false;
        public bool RenderBundles
        {
            get
            {
                return _RenderBundles;
            }
            set
            {
                SetProperty(ref _RenderBundles, value);
            }
        }

        bool _DynamicOffsets = false;
        public bool DynamicOffsets
        {
            get
            {
                return _DynamicOffsets;
            }
            set
            {
                SetProperty(ref _DynamicOffsets, value);
            }
        }

        float _FrameTimeAverage = 0;
        public float FrameTimeAverage
        {
            get
            {
                return _FrameTimeAverage;
            }
            set
            {
                SetProperty(ref _FrameTimeAverage, value);
            }
        }

        float _CpuTimeAverage = 0;
        public float CpuTimeAverage
        {
            get
            {
                return _CpuTimeAverage;
            }
            set
            {
                SetProperty(ref _CpuTimeAverage, value);
            }
        }

        void SetProperty<T>(ref T backendVar, T value, [CallerMemberName] string callerName = "")
        {
            backendVar = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(callerName));
        }
    }
}
