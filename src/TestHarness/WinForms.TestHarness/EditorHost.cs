﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2019, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Chem4Word.ACME;
using Chem4Word.Core;
using Chem4Word.Core.UI.Wpf;
using Chem4Word.Model2;
using Chem4Word.Model2.Converters.CML;
using Chem4Word.Telemetry;
using Application = System.Windows.Application;
using Size = System.Drawing.Size;

namespace WinForms.TestHarness
{
    public partial class EditorHost : Form
    {
        public DialogResult Result = DialogResult.Cancel;

        public string OutputValue { get; set; }
        private string _editorType;

        public EditorHost(string cml, string type)
        {
            InitializeComponent();
            _editorType = type;

            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var settingsPath = Path.Combine(appdata, "Chem4Word.V3");

            var used1D = GetUsedProperties(cml);

            switch (_editorType)
            {
                case "ACME":
                    Options options = new Options();
                    options.SettingsFile = Path.Combine(settingsPath, "Chem4Word.Editor.ACME.json");

                    Editor acmeEditor = new Editor(cml, used1D, options);
                    acmeEditor.InitializeComponent();
                    elementHost1.Child = acmeEditor;

                    // Configure Control
                    acmeEditor.ShowSave = true;
                    acmeEditor.Telemetry = new TelemetryWriter(true);

                    // Wire Up Button(s)
                    acmeEditor.OnOkButtonClick += OnWpfButtonClick;
                    break;

                case "LABELS":
                    LabelsEditor labelsEditor = new LabelsEditor();
                    labelsEditor.InitializeComponent();
                    labelsEditor.Used1D = used1D;
                    labelsEditor.PopulateTreeView(cml);
                    elementHost1.Child = labelsEditor;

                    // Configure Control

                    // Wire Up Button(s)
                    labelsEditor.OnButtonClick += OnWpfButtonClick;
                    break;

                default:
                    CmlEditor cmlEditor = new CmlEditor(cml);
                    cmlEditor.InitializeComponent();
                    elementHost1.Child = cmlEditor;

                    // Configure Control

                    // Wire Up Button(s)
                    cmlEditor.OnButtonClick += OnWpfButtonClick;
                    break;
            }
        }

        private List<string> GetUsedProperties(string cml)
        {
            CMLConverter cc = new CMLConverter();
            Model model = cc.Import(cml);

            List<string> used1D = new List<string>();
            used1D.Add(model.CustomXmlPartGuid);

            foreach (var property in model.AllTextualProperties)
            {
                if (property.FullType != null)
                {
                    if (property.FullType.Equals(CMLConstants.ValueChem4WordLabel)
                        || property.FullType.Equals(CMLConstants.ValueChem4WordFormula)
                        || property.FullType.Equals(CMLConstants.ValueChem4WordSynonym))
                    {
                        used1D.Add($"{property.Id}:{model.CustomXmlPartGuid}");
                    }
                }
            }

            return used1D;
        }

        private void EditorHost_Load(object sender, EventArgs e)
        {
            MinimumSize = new Size(300, 200);

            switch (_editorType)
            {
                case "ACME":
                    if (elementHost1.Child is Editor acmeEditor)
                    {
                        acmeEditor.TopLeft = new Point(Location.X + Chem4Word.Core.Helpers.Constants.TopLeftOffset, Location.Y + Chem4Word.Core.Helpers.Constants.TopLeftOffset);
                    }
                    break;

                case "LABELS":
                    if (elementHost1.Child is LabelsEditor labelsEditor)
                    {
                        labelsEditor.TopLeft = new Point(Location.X + Chem4Word.Core.Helpers.Constants.TopLeftOffset, Location.Y + Chem4Word.Core.Helpers.Constants.TopLeftOffset);
                    }
                    break;

                default:
                    // Do Nothing
                    break;
            }
        }

        private void OnWpfButtonClick(object sender, EventArgs e)
        {
            WpfEventArgs args = (WpfEventArgs)e;
            if (args.Button.Equals("OK") || args.Button.Equals("SAVE"))
            {
                Result = DialogResult.OK;
                OutputValue = args.OutputValue;
            }
            else
            {
                Result = DialogResult.Cancel;
            }

            Hide();
        }

        private void EditorHost_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Result != DialogResult.OK && e.CloseReason == CloseReason.UserClosing)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Do you wish to save your changes?");
                sb.AppendLine("  Click 'Yes' to save your changes and exit.");
                sb.AppendLine("  Click 'No' to discard your changes and exit.");
                sb.AppendLine("  Click 'Cancel' to return to the form.");

                switch (_editorType)
                {
                    case "ACME":
                        if (elementHost1.Child is Editor acmeEditor
                            && acmeEditor.IsDirty)
                        {
                            DialogResult dr = UserInteractions.AskUserYesNoCancel(sb.ToString());
                            switch (dr)
                            {
                                case DialogResult.Cancel:
                                    e.Cancel = true;
                                    break;

                                case DialogResult.Yes:
                                    Result = DialogResult.OK;
                                    CMLConverter cc = new CMLConverter();
                                    OutputValue = cc.Export(acmeEditor.Data);
                                    Hide();
                                    acmeEditor.OnOkButtonClick -= OnWpfButtonClick;
                                    break;

                                case DialogResult.No:
                                    break;
                            }
                        }
                        break;

                    case "LABELS":
                        if (elementHost1.Child is LabelsEditor labelsEditor
                            && labelsEditor.IsDirty)
                        {
                            DialogResult dr = UserInteractions.AskUserYesNoCancel(sb.ToString());
                            switch (dr)
                            {
                                case DialogResult.Cancel:
                                    e.Cancel = true;
                                    break;

                                case DialogResult.Yes:
                                    Result = DialogResult.OK;
                                    CMLConverter cc = new CMLConverter();
                                    OutputValue = cc.Export(labelsEditor.SubModel);
                                    Hide();
                                    labelsEditor.OnButtonClick -= OnWpfButtonClick;
                                    break;

                                case DialogResult.No:
                                    break;
                            }
                        }
                        break;

                    default:
                        if (elementHost1.Child is CmlEditor cmlEditor)
                        {
                            // We don't care just ignore it
                        }
                        break;
                }
            }
        }
    }
}