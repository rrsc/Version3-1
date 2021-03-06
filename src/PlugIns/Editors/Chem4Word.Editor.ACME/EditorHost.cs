﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2020, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Chem4Word.ACME;
using Chem4Word.Core;
using Chem4Word.Core.Helpers;
using Chem4Word.Core.UI.Wpf;
using Chem4Word.Model2.Converters.CML;
using Chem4Word.Model2.Helpers;
using Size = System.Drawing.Size;

namespace Chem4Word.Editor.ACME
{
    public partial class EditorHost : Form
    {
        public Point TopLeft { get; set; }

        public Size FormSize { get; set; }

        public string OutputValue { get; set; }

        private readonly string _cml;
        private readonly List<string> _used1DProperties;
        private readonly AcmeOptions _options;

        private bool IsLoading { get; set; } = true;

        public EditorHost(string cml, List<string> used1DProperties, AcmeOptions options)
        {
            InitializeComponent();

            _cml = cml;
            _used1DProperties = used1DProperties;
            _options = options;
        }

        private void EditorHost_LocationChanged(object sender, EventArgs e)
        {
            if (!IsLoading)
            {
                TopLeft = new Point(Left + Constants.TopLeftOffset / 2, Top + Constants.TopLeftOffset / 2);
                if (elementHost1.Child is Chem4Word.ACME.Editor editor)
                {
                    editor.TopLeft = TopLeft;
                }
            }
        }

        private void EditorHost_Load(object sender, EventArgs e)
        {
            IsLoading = true;

            if (TopLeft.X != 0 && TopLeft.Y != 0)
            {
                Left = (int)TopLeft.X;
                Top = (int)TopLeft.Y;
            }

            MinimumSize = new Size(900, 600);

            if (FormSize.Width != 0 && FormSize.Height != 0)
            {
                Width = FormSize.Width;
                Height = FormSize.Height;
            }

            // Fix bottom panel
            int margin = Buttons.Height - Save.Bottom;
            splitContainer1.SplitterDistance = splitContainer1.Height - Save.Height - margin * 2;
            splitContainer1.FixedPanel = FixedPanel.Panel2;
            splitContainer1.IsSplitterFixed = true;

            // Set Up WPF UC
            if (elementHost1.Child is Chem4Word.ACME.Editor editor)
            {
                editor.SetProperties(_cml, _used1DProperties, _options);
                editor.TopLeft = TopLeft;
                editor.ShowFeedback = false;
                editor.OnFeedbackChange += AcmeEditorOnFeedbackChange;
                var model = editor.ActiveViewModel.Model;
                if (model == null || model.Molecules.Count == 0)
                {
                    Text = "ACME - New structure";
                }
                else
                {
                    List<MoleculeFormulaPart> parts = FormulaHelper.ParseFormulaIntoParts(editor.ActiveViewModel.Model.ConciseFormula);
                    var x = FormulaHelper.FormulaPartsAsUnicode(parts);
                    Text = "ACME - Editing " + x;
                }
            }

            IsLoading = false;
        }

        private void AcmeEditorOnFeedbackChange(object sender, WpfEventArgs e)
        {
            MessageFromWpf.Text = e.OutputValue;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            CMLConverter cc = new CMLConverter();
            DialogResult = DialogResult.Cancel;

            if (elementHost1.Child is Chem4Word.ACME.Editor editor
                && editor.IsDirty)
            {
                DialogResult = DialogResult.OK;
                OutputValue = cc.Export(editor.EditedModel);
            }
            Hide();
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Hide();
        }

        private void EditorHost_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            {
                if (elementHost1.Child is Chem4Word.ACME.Editor editor
                    && editor.IsDirty)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("Do you wish to save your changes?");
                    sb.AppendLine("  Click 'Yes' to save your changes and exit.");
                    sb.AppendLine("  Click 'No' to discard your changes and exit.");
                    sb.AppendLine("  Click 'Cancel' to return to the form.");
                    DialogResult dr = UserInteractions.AskUserYesNoCancel(sb.ToString());
                    switch (dr)
                    {
                        case DialogResult.Cancel:
                            e.Cancel = true;
                            break;

                        case DialogResult.Yes:
                            DialogResult = DialogResult.OK;
                            var model = editor.EditedModel;
                            model.RescaleForCml();
                            // Replace any temporary Ids which are Guids
                            model.ReLabelGuids();
                            CMLConverter cc = new CMLConverter();
                            OutputValue = cc.Export(model);
                            Hide();
                            editor = null;
                            break;

                        case DialogResult.No:
                            editor = null;
                            break;
                    }
                }
            }
        }
    }
}