using System;
using System.Diagnostics;

namespace Dupples_finder_UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static MainWindow _inst;
        public MainWindow()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
            }
            _inst = this;
        }

        public static void Update()
        {
            //_inst.Table1.UpdateLayout();
        }
    }
}
