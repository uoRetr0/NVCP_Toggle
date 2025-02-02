using System;
using System.Windows.Forms;

namespace NVCP_Toggle
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application failed to start: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                              "Startup Error", 
                              MessageBoxButtons.OK, 
                              MessageBoxIcon.Error);
            }
        }

    }
}
