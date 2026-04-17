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
    public partial class Pay : Form
    {
        int num;
        int count;
        public Pay(int num)
        {
            InitializeComponent();
            this.num = num;

            UserControl payUC = null;

            switch (num)
            {
                case 0:
                    // 취소
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                    return;

                case 1:
                    Credit credit = new Credit();
                    // 결제 완료 → DialogResult.OK
                    credit.OnConfirmed += () =>
                    {
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };
                    // 타임아웃/취소 → DialogResult.Cancel
                    credit.OnCancelled += () =>
                    {
                        this.DialogResult = DialogResult.Cancel;
                        this.Close();
                    };
                    payUC = credit;
                    break;

                case 2:
                    KaKao kakao = new KaKao();
                    kakao.OnConfirmed += () =>
                    {
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };
                    kakao.OnCancelled += () =>
                    {
                        this.DialogResult = DialogResult.Cancel;
                        this.Close();
                    };
                    payUC = kakao;
                    break;

                case 3:
                    Npay npay = new Npay();
                    npay.OnConfirmed += () =>
                    {
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };
                    npay.OnCancelled += () =>
                    {
                        this.DialogResult = DialogResult.Cancel;
                        this.Close();
                    };
                    payUC = npay;
                    break;
            }

            if (payUC != null)
            {
                payUC.Dock = DockStyle.Fill;
                this.Controls.Add(payUC);
            }
        }
    }
}
