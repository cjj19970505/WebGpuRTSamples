using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;

namespace Animometer
{
    // http://www.mobilemotion.eu/?p=1680
    public class StringFormatConverter:IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var format = parameter as string;
            if (!String.IsNullOrEmpty(format))
                return String.Format(format, value);

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
