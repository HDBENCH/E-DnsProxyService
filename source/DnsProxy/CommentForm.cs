﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DnsProxyAdmin
{
    public partial class CommentForm : Form
    {
        public string comment;

        public CommentForm ()
        {
            InitializeComponent ();
        }

        private void CommentForm_Load (object sender, EventArgs e)
        {
            this.textBox1.Text = this.comment;
            this.textBox1.Focus();
            this.textBox1.Select();
        }

        private void textBox1_TextChanged (object sender, EventArgs e)
        {
            this.comment = this.textBox1.Text;
        }
    }
}
