using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kiosk_Sum
{
    public partial class Npay : UserControl
    {
        public event Action OnConfirmed;
        public event Action OnCancelled;

        int count;

        public Npay()
        {
            InitializeComponent();
            btnComplete.Click += (s, e) => ConfirmPayment();
        }

        private void Npay_Load(object sender, EventArgs e)
        {
            timer1.Interval = 1000;
            timer2.Interval = 1000;
            timer1.Start();
            timer2.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            count++;
            if (count >= 30)
            {
                timer1.Stop();
                OnCancelled?.Invoke();
            }
        }

        public void ConfirmPayment()
        {
            timer1.Stop();
            OnConfirmed?.Invoke();
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            bigLabel2.Text = "00:" + (30 - count).ToString("D2");
            if (count > 15 && count < 25)
            {
                bigLabel2.ForeColor = Color.Yellow;
            }
            else if (count > 25)
            {
                bigLabel2.ForeColor = Color.Red;
            }
        }
    }
}
