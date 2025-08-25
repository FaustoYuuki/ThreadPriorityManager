using System;
using System.Windows.Forms;

namespace ThreadPriorityManager
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erro ao iniciar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}