﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2018, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;

namespace Chem4Word.ACME
{
    /// <summary>
    /// Interaction logic for Editor.xaml
    /// </summary>
    public partial class Editor : UserControl
    {
        public delegate void EventHandler(object sender, WpfEventArgs args);

        public event EventHandler OnOkButtonClick;

        public Editor()
        {
            InitializeComponent();
        }

        public Editor(string cml)
        {
            InitializeComponent();
            cmlText.Text = cml;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            WpfEventArgs args = new WpfEventArgs();
            args.OutputValue = cmlText.Text;
            args.Button = "OK";

            OnOkButtonClick?.Invoke(this, args);
        }
    }

    public class WpfEventArgs : EventArgs
    {
        public string Button { get; set; }
        public string OutputValue { get; set; }
    }
}