using System;
using System.Windows.Forms;

namespace IndigoQaClient
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // ????? ?????? ????????? (???? ?-WinForms)
            ApplicationConfiguration.Initialize();

            // ???? ????? ????? ?????? (Form1)
            Application.Run(new Form1());
        }
    }
}