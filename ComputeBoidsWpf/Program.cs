using System;
using System.Collections.Generic;
using System.Text;

namespace ComputeBoidsWpf
{
    
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            using(new SampleUwpApp.App())
            {
                App app = new App();
                app.InitializeComponent();
                app.Run();
            }
        }
    }
}
