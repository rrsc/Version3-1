﻿// ---------------------------------------------------------------------------
//  Copyright (c) 2020, The .NET Foundation.
//  This software is released under the Apache License, Version 2.0.
//  The license and further copyright text can be found in the file LICENSE.md
//  at the root directory of the distribution.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Chem4Word.Core.Helpers;
using Chem4Word.Core.UI.Forms;
using Chem4Word.Model2;
using Chem4Word.Model2.Helpers;
using Chem4Word.Renderer.OoXmlV4.Entities;
using Chem4Word.Renderer.OoXmlV4.Enums;
using Chem4Word.Renderer.OoXmlV4.TTF;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office.Drawing;
using DocumentFormat.OpenXml.Office2010.Word;
using DocumentFormat.OpenXml.Wordprocessing;
using IChem4Word.Contracts;
using Newtonsoft.Json;
using A = DocumentFormat.OpenXml.Drawing;
using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using NonVisualDrawingProperties = DocumentFormat.OpenXml.Office2010.Word.NonVisualDrawingProperties;
using Point = System.Windows.Point;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Wpg = DocumentFormat.OpenXml.Office2010.Word.DrawingGroup;
using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

namespace Chem4Word.Renderer.OoXmlV4.OOXML
{
    public class OoXmlRenderer
    {
        // DrawingML Units
        // https://startbigthinksmall.wordpress.com/2010/01/04/points-inches-and-emus-measuring-units-in-office-open-xml/
        // EMU Calculator
        // http://lcorneliussen.de/raw/dashboards/ooxml/

        private static string _product = Assembly.GetExecutingAssembly().FullName.Split(',')[0];
        private static string _class = MethodBase.GetCurrentMethod().DeclaringType?.Name;

        private Wpg.WordprocessingGroup _wordprocessingGroup;
        private long _ooxmlId;
        private Rect _boundingBoxOfEverything;
        private Rect _boundingBoxOfAllAtoms;

        // Inputs to positioner
        private Dictionary<char, TtfCharacter> _TtfCharacterSet;

        private OoXmlV4Options _options;
        private IChem4WordTelemetry _telemetry;
        private Point _topLeft;
        private Model _chemistryModel;
        private double _medianBondLength;

        // Outputs of positioner
        private List<AtomLabelCharacter> _atomLabelCharacters = new List<AtomLabelCharacter>();
        private List<BondLine> _bondLines = new List<BondLine>();
        private List<MoleculeExtents> _allMoleculeExtents = new List<MoleculeExtents>();
        private List<OoXmlString> _moleculeLabels = new List<OoXmlString>();
        private List<Rect> _moleculeBrackets = new List<Rect>();
        private List<Rect> _groupBrackets = new List<Rect>();
        private List<Point> _ringCentres = new List<Point>();
        private Dictionary<string, List<Point>> _convexHulls = new Dictionary<string, List<Point>>();

        public OoXmlRenderer(Model model, OoXmlV4Options options, IChem4WordTelemetry telemetry, Point topLeft)
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";

            _telemetry = telemetry;
            _telemetry.Write(module, "Verbose", "Called");

            _options = options;
            _topLeft = topLeft;
            _chemistryModel = model;
            _medianBondLength = model.MeanBondLength;

            LoadFont();

            _boundingBoxOfAllAtoms = _chemistryModel.BoundingBoxOfCmlPoints;
        }

        public Run GenerateRun()
        {
            string module = $"{_product}.{_class}.{MethodBase.GetCurrentMethod().Name}()";
            _telemetry.Write(module, "Verbose", "Called");

            Stopwatch swr = new Stopwatch();
            swr.Start();

            // Initialise OoXml Object counter
            _ooxmlId = 1;

            //set the median bond length
            _medianBondLength = _chemistryModel.MeanBondLength;
            if (_chemistryModel.GetAllBonds().Count == 0)
            {
                _medianBondLength = _options.BondLength;
            }

            // Initialise progress monitoring
            Progress progress = new Progress
            {
                TopLeft = _topLeft
            };

            var positioner = new OoXmlPositioner(new PositionerInputs
            {
                Progress = progress,
                Options = _options,
                TtfCharacterSet = _TtfCharacterSet,
                Telemetry = _telemetry,
                MeanBondLength = _medianBondLength,
                Model = _chemistryModel,
            });

            var positionerOutputs = positioner.Position();

            _atomLabelCharacters = positionerOutputs.AtomLabelCharacters;
            _bondLines = positionerOutputs.BondLines;
            _convexHulls = positionerOutputs.ConvexHulls;
            _ringCentres = positionerOutputs.RingCenters;
            _allMoleculeExtents = positionerOutputs.AllMoleculeExtents;
            _groupBrackets = positionerOutputs.GroupBrackets;
            _moleculeBrackets = positionerOutputs.MoleculeBrackets;
            _moleculeLabels = positionerOutputs.MoleculeLabels;

            // 6.1  Calculate canvas size
            SetCanvasSize();

            // 6.2  Create Base OoXml Objects
            Run run = CreateRun();

            // 7.   Render Brackets
            // Render molecule grouping brackets
            if (_options.ShowMoleculeGrouping)
            {
                foreach (var group in _groupBrackets)
                {
                    string bracketColour = _options.ColouredAtoms ? "00bbff" : "000000";
                    DrawGroupBrackets(group, _medianBondLength * 0.5, OoXmlHelper.ACS_LINE_WIDTH * 2, bracketColour);
                }
            }

            // Render molecule brackets
            foreach (var moleculeBracket in _moleculeBrackets)
            {
                DrawMoleculeBrackets(moleculeBracket, _medianBondLength * 0.2, OoXmlHelper.ACS_LINE_WIDTH, "000000");
            }

            // 8.   Render Diagnostic Markers
            if (_options.ShowMoleculeBoundingBoxes)
            {
                foreach (var item in _allMoleculeExtents)
                {
                    DrawBox(item.AtomExtents, "ff0000", .25);
                    DrawBox(item.InternalCharacterExtents, "00ff00", .25);
                    DrawBox(item.ExternalCharacterExtents, "0000ff", .25);
                }

                DrawBox(_boundingBoxOfAllAtoms, "ff0000", .25);
                DrawBox(_boundingBoxOfEverything, "000000", .25);
            }

            if (_options.ShowHulls)
            {
                foreach (var hull in _convexHulls)
                {
                    var points = hull.Value.ToList();
                    DrawPolygon(points, "ff0000", 0.25);
                }
            }

            if (_options.ShowCharacterBoundingBoxes)
            {
                foreach (var atom in _chemistryModel.GetAllAtoms())
                {
                    List<AtomLabelCharacter> chars = _atomLabelCharacters.FindAll(a => a.ParentAtom.Equals(atom.Path));
                    Rect atomCharsRect = Rect.Empty;
                    foreach (var alc in chars)
                    {
                        Rect thisBoundingBox = thisBoundingBox = new Rect(alc.Position,
                                                                          new Size(OoXmlHelper.ScaleCsTtfToCml(alc.Character.Width, _medianBondLength),
                                                                                   OoXmlHelper.ScaleCsTtfToCml(alc.Character.Height, _medianBondLength)));
                        if (alc.IsSmaller)
                        {
                            thisBoundingBox = new Rect(alc.Position,
                                                       new Size(OoXmlHelper.ScaleCsTtfToCml(alc.Character.Width, _medianBondLength) * OoXmlHelper.SUBSCRIPT_SCALE_FACTOR,
                                                                OoXmlHelper.ScaleCsTtfToCml(alc.Character.Height, _medianBondLength) * OoXmlHelper.SUBSCRIPT_SCALE_FACTOR));
                        }

                        DrawBox(thisBoundingBox, "00ff00", 0.25);

                        atomCharsRect.Union(thisBoundingBox);
                    }

                    if (!atomCharsRect.IsEmpty)
                    {
                        DrawBox(atomCharsRect, "ffa500", 0.5);
                    }
                }
            }

            double spotSize = _medianBondLength * OoXmlHelper.MULTIPLE_BOND_OFFSET_PERCENTAGE / 3;

            if (_options.ShowRingCentres)
            {
                foreach (var point in _ringCentres)
                {
                    Rect extents = new Rect(new Point(point.X - spotSize, point.Y - spotSize),
                                       new Point(point.X + spotSize, point.Y + spotSize));
                    DrawShape(extents, A.ShapeTypeValues.Ellipse, "00ff00");
                }
            }

            if (_options.ShowAtomPositions)
            {
                foreach (var atom in _chemistryModel.GetAllAtoms())
                {
                    Rect extents = new Rect(new Point(atom.Position.X - spotSize, atom.Position.Y - spotSize),
                                            new Point(atom.Position.X + spotSize, atom.Position.Y + spotSize));
                    DrawShape(extents, A.ShapeTypeValues.Ellipse, "ff0000");
                }
            }

            if (_options.ShowHulls)
            {
                foreach (var hull in _convexHulls)
                {
                    var points = hull.Value.ToList();
                    DrawPolygon(points, "ff0000", 0.25);
                }
            }

            // 9.   Render Bond Lines
            foreach (var bondLine in _bondLines)
            {
                switch (bondLine.Style)
                {
                    case BondLineStyle.Wedge:
                        DrawWedgeBond(CalculateWedgeOutline(bondLine), bondLine.BondPath, bondLine.Colour);
                        break;

                    case BondLineStyle.Hatch:
                        DrawHatchBond(CalculateWedgeOutline(bondLine), bondLine.BondPath, bondLine.Colour);
                        break;

                    default:
                        DrawBondLine(bondLine.Start, bondLine.End, bondLine.BondPath, bondLine.Style, bondLine.Colour);
                        break;
                }
            }

            // 10.  Render Atom and Molecule Characters
            foreach (var character in _atomLabelCharacters)
            {
                DrawCharacter(character);
            }

            // 11.  Render Molecule Labels - Experimental - Does not work well
            //foreach (var moleculeLabel in _moleculeLabels)
            //{
            //    DrawBox(moleculeLabel.Extents, "ff0000", 0.25);
            //    DrawTextBox(moleculeLabel.Extents, moleculeLabel.Value, moleculeLabel.Colour);
            //}

            _telemetry.Write(module, "Timing", $"Rendering {_chemistryModel.Molecules.Count} molecules with {_chemistryModel.TotalAtomsCount} atoms and {_chemistryModel.TotalBondsCount} bonds took {swr.ElapsedMilliseconds.ToString("##,##0", CultureInfo.InvariantCulture)} ms; Average Bond Length: {_chemistryModel.MeanBondLength.ToString("#0.00", CultureInfo.InvariantCulture)}");

            ShutDownProgress(progress);

            return run;
        }

        public void DrawCharacter(AtomLabelCharacter alc)
        {
            Point characterPosition = new Point(alc.Position.X, alc.Position.Y);
            characterPosition.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);

            Int64Value emuWidth = OoXmlHelper.ScaleCsTtfToEmu(alc.Character.Width, _medianBondLength);
            Int64Value emuHeight = OoXmlHelper.ScaleCsTtfToEmu(alc.Character.Height, _medianBondLength);
            if (alc.IsSmaller)
            {
                emuWidth = OoXmlHelper.ScaleCsTtfSubScriptToEmu(alc.Character.Width, _medianBondLength);
                emuHeight = OoXmlHelper.ScaleCsTtfSubScriptToEmu(alc.Character.Height, _medianBondLength);
            }
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(characterPosition.Y);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(characterPosition.X);

            //Debug.WriteLine($"Character {alc.Character.Character} T: {emuTop}, L: {emuLeft}, W: {emuWidth}, H: {emuHeight}");

            string parent = alc.ParentAtom.Equals(alc.ParentMolecule) ? alc.ParentMolecule : alc.ParentAtom;
            string shapeName = $"Character {alc.Character.Character} of {parent}";
            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId++, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            foreach (TtfContour contour in alc.Character.Contours)
            {
                int i = 0;

                while (i < contour.Points.Count)
                {
                    TtfPoint thisPoint = contour.Points[i];
                    TtfPoint nextPoint = null;
                    if (i < contour.Points.Count - 1)
                    {
                        nextPoint = contour.Points[i + 1];
                    }

                    switch (thisPoint.Type)
                    {
                        case TtfPoint.PointType.Start:
                            A.MoveTo moveTo = new A.MoveTo();
                            if (alc.IsSmaller)
                            {
                                A.Point point = MakeSubscriptPoint(thisPoint);
                                moveTo.Append(point);
                                path.Append(moveTo);
                            }
                            else
                            {
                                A.Point point = MakeNormalPoint(thisPoint);
                                moveTo.Append(point);
                                path.Append(moveTo);
                            }
                            i++;
                            break;

                        case TtfPoint.PointType.Line:
                            A.LineTo lineTo = new A.LineTo();
                            if (alc.IsSmaller)
                            {
                                A.Point point = MakeSubscriptPoint(thisPoint);
                                lineTo.Append(point);
                                path.Append(lineTo);
                            }
                            else
                            {
                                A.Point point = MakeNormalPoint(thisPoint);
                                lineTo.Append(point);
                                path.Append(lineTo);
                            }
                            i++;
                            break;

                        case TtfPoint.PointType.CurveOff:
                            A.QuadraticBezierCurveTo quadraticBezierCurveTo = new A.QuadraticBezierCurveTo();
                            if (alc.IsSmaller)
                            {
                                A.Point pointA = MakeSubscriptPoint(thisPoint);
                                A.Point pointB = MakeSubscriptPoint(nextPoint);
                                quadraticBezierCurveTo.Append(pointA);
                                quadraticBezierCurveTo.Append(pointB);
                                path.Append(quadraticBezierCurveTo);
                            }
                            else
                            {
                                A.Point pointA = MakeNormalPoint(thisPoint);
                                A.Point pointB = MakeNormalPoint(nextPoint);
                                quadraticBezierCurveTo.Append(pointA);
                                quadraticBezierCurveTo.Append(pointB);
                                path.Append(quadraticBezierCurveTo);
                            }
                            i++;
                            i++;
                            break;

                        case TtfPoint.PointType.CurveOn:
                            // Should never get here !
                            i++;
                            break;
                    }
                }

                A.CloseShapePath closeShapePath = new A.CloseShapePath();
                path.Append(closeShapePath);
            }

            pathList.Append(path);

            // End of the lines

            A.SolidFill solidFill = new A.SolidFill();

            // Set Colour
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = alc.Colour };
            solidFill.Append(rgbColorModelHex);

            shapeProperties.Append(CreateCustomGeometry(pathList));
            shapeProperties.Append(solidFill);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);

            // Local Functions
            A.Point MakeSubscriptPoint(TtfPoint ttfPoint)
            {
                A.Point pp = new A.Point
                {
                    X = $"{OoXmlHelper.ScaleCsTtfSubScriptToEmu(ttfPoint.X - alc.Character.OriginX, _medianBondLength)}",
                    Y = $"{OoXmlHelper.ScaleCsTtfSubScriptToEmu(alc.Character.Height + ttfPoint.Y - (alc.Character.Height + alc.Character.OriginY), _medianBondLength)}"
                };
                return pp;
            }

            A.Point MakeNormalPoint(TtfPoint ttfPoint)
            {
                A.Point pp = new A.Point
                {
                    X = $"{OoXmlHelper.ScaleCsTtfToEmu(ttfPoint.X - alc.Character.OriginX, _medianBondLength)}",
                    Y = $"{OoXmlHelper.ScaleCsTtfToEmu(alc.Character.Height + ttfPoint.Y - (alc.Character.Height + alc.Character.OriginY), _medianBondLength)}"
                };
                return pp;
            }
        }

        private void DrawBondLine(Point bondStart, Point bondEnd, string bondPath,
                                  BondLineStyle lineStyle = BondLineStyle.Solid,
                                  string colour = "000000",
                                  double lineWidth = OoXmlHelper.ACS_LINE_WIDTH)
        {
            switch (lineStyle)
            {
                case BondLineStyle.Solid:
                case BondLineStyle.Dotted:
                case BondLineStyle.Dashed:
                    DrawStraightLine(bondStart, bondEnd, bondPath, lineStyle, colour, lineWidth);
                    break;

                case BondLineStyle.Wavy:
                    DrawWavyLine(bondStart, bondEnd, bondPath, colour);
                    break;

                default:
                    DrawStraightLine(bondStart, bondEnd, bondPath, BondLineStyle.Dotted, "00ff00", lineWidth);
                    break;
            }
        }

        private List<SimpleLine> CreateHatchLines(List<Point> points)
        {
            List<SimpleLine> lines = new List<SimpleLine>();

            Point wedgeStart = points[0];
            Point wedgeEndMiddle = points[2];

            // Vector pointing from wedgeStart to wedgeEndMiddle
            Vector direction = wedgeEndMiddle - wedgeStart;
            Matrix rightAngles = new Matrix();
            rightAngles.Rotate(90);
            Vector perpendicular = direction * rightAngles;

            Vector step = direction;
            step.Normalize();
            step *= OoXmlHelper.ScaleCmlToEmu(15 * OoXmlHelper.MULTIPLE_BOND_OFFSET_PERCENTAGE);

            int steps = (int)Math.Ceiling(direction.Length / step.Length);
            double stepLength = direction.Length / steps;

            step.Normalize();
            step *= stepLength;

            Point p0 = wedgeStart + step;
            Point p1 = p0 + perpendicular;
            Point p2 = p0 - perpendicular;

            var r = GeometryTool.ClipLineWithPolygon(p1, p2, points, out _);
            while (r.Length > 2)
            {
                if (r.Length == 4)
                {
                    lines.Add(new SimpleLine(r[1], r[2]));
                }

                if (r.Length == 6)
                {
                    lines.Add(new SimpleLine(r[1], r[2]));
                    lines.Add(new SimpleLine(r[3], r[4]));
                }

                p0 = p0 + step;
                p1 = p0 + perpendicular;
                p2 = p0 - perpendicular;

                r = GeometryTool.ClipLineWithPolygon(p1, p2, points, out _);
            }

            // Define Tail Lines
            lines.Add(new SimpleLine(wedgeEndMiddle, points[1]));
            lines.Add(new SimpleLine(wedgeEndMiddle, points[3]));

            return lines;
        }

        private void DrawTextBox(Rect cmlExtents, string value, string colour)
        {
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);

            Point location = new Point(emuLeft, emuTop);
            Size size = new Size(emuWidth, emuHeight);
            location.Offset(OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Left), OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Top));
            Rect boundingBox = new Rect(location, size);

            emuWidth = (Int64Value)boundingBox.Width;
            emuHeight = (Int64Value)boundingBox.Height;
            emuTop = (Int64Value)boundingBox.Top;
            emuLeft = (Int64Value)boundingBox.Left;

            UInt32Value id = UInt32Value.FromUInt32((uint)_ooxmlId++);
            string shapeName = "String " + id;
            Wps.WordprocessingShape wordprocessingShape = CreateShape(id, shapeName);

            Wps.ShapeProperties shapeProperties = new Wps.ShapeProperties();

            A.Transform2D transform2D = new A.Transform2D();
            A.Offset offset = new A.Offset { X = emuLeft, Y = emuTop };
            A.Extents extents = new A.Extents { Cx = emuWidth, Cy = emuHeight };
            transform2D.Append(offset);
            transform2D.Append(extents);
            shapeProperties.Append(transform2D);

            A.AdjustValueList adjustValueList = new A.AdjustValueList();
            A.PresetGeometry presetGeometry = new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle };
            presetGeometry.Append(adjustValueList);
            shapeProperties.Append(presetGeometry);

            // The TextBox

            Wps.TextBoxInfo2 textBoxInfo2 = new Wps.TextBoxInfo2();
            TextBoxContent textBoxContent = new TextBoxContent();
            textBoxInfo2.Append(textBoxContent);

            // The Paragrah
            Paragraph paragraph = new Paragraph();
            textBoxContent.Append(paragraph);

            ParagraphProperties paragraphProperties = new ParagraphProperties();
            Justification justification = new Justification { Val = JustificationValues.Center };
            paragraphProperties.Append(justification);

            paragraph.Append(paragraphProperties);

            // Now for the text Run
            Run run = new Run();
            paragraph.Append(run);
            RunProperties runProperties = new RunProperties();
            runProperties.Append(CommonRunProperties());

            run.Append(runProperties);

            Text text = new Text(value);
            run.Append(text);

            wordprocessingShape.Append(shapeProperties);
            wordprocessingShape.Append(textBoxInfo2);

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties { LeftInset = 0, TopInset = 0, RightInset = 0, BottomInset = 0 };
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);

            OpenXmlElement[] CommonRunProperties()
            {
                var result = new List<OpenXmlElement>();

                var pointSize = OoXmlHelper.EmusPerCsTtfPoint(_medianBondLength) * 2;

                RunFonts runFonts = new RunFonts { Ascii = "Arial", HighAnsi = "Arial"};
                result.Add(runFonts);

                Color color = new Color { Val = colour };
                result.Add(color);

                FontSize fontSize1 = new FontSize { Val = pointSize.ToString("0") };
                result.Add(fontSize1);

                return result.ToArray();
            }
        }


        private void DrawShape(Rect cmlExtents, A.ShapeTypeValues shape, string colour)
        {
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);

            Point location = new Point(emuLeft, emuTop);
            Size size = new Size(emuWidth, emuHeight);
            location.Offset(OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Left), OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Top));
            Rect boundingBox = new Rect(location, size);

            emuWidth = (Int64Value)boundingBox.Width;
            emuHeight = (Int64Value)boundingBox.Height;
            emuTop = (Int64Value)boundingBox.Top;
            emuLeft = (Int64Value)boundingBox.Left;

            UInt32Value id = UInt32Value.FromUInt32((uint)_ooxmlId++);
            string shapeName = "Shape" + id;
            Wps.WordprocessingShape wordprocessingShape = CreateShape(id, shapeName);

            Wps.ShapeProperties shapeProperties = new Wps.ShapeProperties();

            A.Transform2D transform2D = new A.Transform2D();
            A.Offset offset = new A.Offset { X = emuLeft, Y = emuTop };
            A.Extents extents = new A.Extents { Cx = emuWidth, Cy = emuHeight };
            transform2D.Append(offset);
            transform2D.Append(extents);
            shapeProperties.Append(transform2D);

            A.AdjustValueList adjustValueList = new A.AdjustValueList();
            A.PresetGeometry presetGeometry = new A.PresetGeometry { Preset = shape };
            presetGeometry.Append(adjustValueList);
            shapeProperties.Append(presetGeometry);

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = colour };
            solidFill.Append(rgbColorModelHex);
            shapeProperties.Append(solidFill);

            wordprocessingShape.Append(shapeProperties);
            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawHatchBond(List<Point> points, string bondPath,
                                   string colour = "000000")
        {
            Rect cmlExtents = new Rect(points[0], points[points.Count - 1]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                cmlExtents.Union(new Rect(points[i], points[i + 1]));
            }

            // Move Extents to have 0,0 Top Left Reference
            cmlExtents.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);

            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);

            string shapeName = "Hatch " + bondPath;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId++, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            // Draw a small circle for the starting point
            var xx = 0.5;
            Rect extents = new Rect(new Point(points[0].X - xx, points[0].Y - xx), new Point(points[0].X + xx, points[0].Y + xx));
            DrawShape(extents, A.ShapeTypeValues.Ellipse, colour);

            // Pre offset and scale the extents
            var scaledPoints = new List<Point>();
            foreach (var point in points)
            {
                point.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);
                point.Offset(-cmlExtents.Left, -cmlExtents.Top);
                scaledPoints.Add(new Point(OoXmlHelper.ScaleCmlToEmu(point.X), OoXmlHelper.ScaleCmlToEmu(point.Y)));
            }

            var lines = CreateHatchLines(scaledPoints);

            foreach (var line in lines)
            {
                A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

                A.MoveTo moveTo = new A.MoveTo();
                A.Point startPoint = new A.Point
                {
                    X = line.Start.X.ToString("0"),
                    Y = line.Start.Y.ToString("0")
                };

                moveTo.Append(startPoint);
                path.Append(moveTo);

                A.LineTo lineTo = new A.LineTo();
                A.Point endPoint = new A.Point
                {
                    X = line.End.X.ToString("0"),
                    Y = line.End.Y.ToString("0")
                };
                lineTo.Append(endPoint);
                path.Append(lineTo);

                pathList.Append(path);
            }

            // End of the lines

            shapeProperties.Append(CreateCustomGeometry(pathList));

            // Set shape fill colour
            A.SolidFill insideFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = colour };
            insideFill.Append(rgbColorModelHex);

            shapeProperties.Append(insideFill);

            // Set shape outline colour
            A.Outline outline = new A.Outline { Width = Int32Value.FromInt32((int)OoXmlHelper.ACS_LINE_WIDTH_EMUS), CapType = A.LineCapValues.Round };
            A.RgbColorModelHex rgbColorModelHex2 = new A.RgbColorModelHex { Val = colour };
            A.SolidFill outlineFill = new A.SolidFill();
            outlineFill.Append(rgbColorModelHex2);
            outline.Append(outlineFill);

            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private A.Point MakePoint(Point pp, Rect cmlExtents)
        {
            pp.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);
            pp.Offset(-cmlExtents.Left, -cmlExtents.Top);
            return new A.Point
            {
                X = $"{OoXmlHelper.ScaleCmlToEmu(pp.X)}",
                Y = $"{OoXmlHelper.ScaleCmlToEmu(pp.Y)}"
            };
        }

        private void DrawWedgeBond(List<Point> points, string bondPath,
                                   string colour = "000000")
        {
            Rect cmlExtents = new Rect(points[0], points[points.Count - 1]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                cmlExtents.Union(new Rect(points[i], points[i + 1]));
            }

            // Move Extents to have 0,0 Top Left Reference
            cmlExtents.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);

            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);

            string shapeName = "Wedge " + bondPath;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId++, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            A.MoveTo moveTo = new A.MoveTo();
            moveTo.Append(MakePoint(points[0], cmlExtents));
            path.Append(moveTo);

            for (int i = 1; i < points.Count; i++)
            {
                A.LineTo lineTo = new A.LineTo();
                lineTo.Append(MakePoint(points[i], cmlExtents));
                path.Append(lineTo);
            }

            A.CloseShapePath closeShapePath = new A.CloseShapePath();
            path.Append(closeShapePath);

            pathList.Append(path);

            // End of the lines

            shapeProperties.Append(CreateCustomGeometry(pathList));

            // Set shape fill colour
            A.SolidFill insideFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = colour };
            insideFill.Append(rgbColorModelHex);

            shapeProperties.Append(insideFill);

            // Set shape outline colour
            A.Outline outline = new A.Outline { Width = Int32Value.FromInt32((int)OoXmlHelper.ACS_LINE_WIDTH_EMUS), CapType = A.LineCapValues.Round };
            A.RgbColorModelHex rgbColorModelHex2 = new A.RgbColorModelHex { Val = colour };
            A.SolidFill outlineFill = new A.SolidFill();
            outlineFill.Append(rgbColorModelHex2);
            outline.Append(outlineFill);

            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawMoleculeBrackets(Rect cmlExtents, double armLength, double lineWidth, string lineColour)
        {
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);

            Point location = new Point(emuLeft, emuTop);
            Size size = new Size(emuWidth, emuHeight);
            location.Offset(OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Left), OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Top));
            Rect boundingBox = new Rect(location, size);
            Int64Value armLengthEmu = OoXmlHelper.ScaleCmlToEmu(armLength);

            emuWidth = (Int64Value)boundingBox.Width;
            emuHeight = (Int64Value)boundingBox.Height;
            emuTop = (Int64Value)boundingBox.Top;
            emuLeft = (Int64Value)boundingBox.Left;

            string shapeName = "Box " + _ooxmlId++;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            double gap = boundingBox.Width * 0.8;
            double leftSide = (emuWidth - gap) / 2;
            double rightSide = emuWidth - leftSide;

            // Left Path
            A.Path path1 = new A.Path { Width = emuWidth, Height = emuHeight };

            A.MoveTo moveTo = new A.MoveTo();
            A.Point point1 = new A.Point { X = leftSide.ToString("0"), Y = "0" };
            moveTo.Append(point1);

            // Mid Point
            A.LineTo lineTo1 = new A.LineTo();
            A.Point point2 = new A.Point { X = "0", Y = "0" };
            lineTo1.Append(point2);

            // Last Point
            A.LineTo lineTo2 = new A.LineTo();
            A.Point point3 = new A.Point { X = "0", Y = boundingBox.Height.ToString("0") };
            lineTo2.Append(point3);

            // Mid Point
            A.LineTo lineTo3 = new A.LineTo();
            A.Point point4 = new A.Point { X = leftSide.ToString("0"), Y = boundingBox.Height.ToString("0") };
            lineTo3.Append(point4);

            path1.Append(moveTo);
            path1.Append(lineTo1);
            path1.Append(lineTo2);
            path1.Append(lineTo3);

            pathList.Append(path1);

            // Right Path
            A.Path path2 = new A.Path { Width = emuWidth, Height = emuHeight };

            A.MoveTo moveTo2 = new A.MoveTo();
            A.Point point5 = new A.Point { X = rightSide.ToString("0"), Y = "0" };
            moveTo2.Append(point5);

            // Mid Point
            A.LineTo lineTo4 = new A.LineTo();
            A.Point point6 = new A.Point { X = boundingBox.Width.ToString("0"), Y = "0" };
            lineTo4.Append(point6);

            // Last Point
            A.LineTo lineTo5 = new A.LineTo();
            A.Point point7 = new A.Point { X = boundingBox.Width.ToString("0"), Y = boundingBox.Height.ToString("0") };
            lineTo5.Append(point7);

            // Mid Point
            A.LineTo lineTo6 = new A.LineTo();
            A.Point point8 = new A.Point { X = rightSide.ToString("0"), Y = boundingBox.Height.ToString("0") };
            lineTo6.Append(point8);

            path2.Append(moveTo2);
            path2.Append(lineTo4);
            path2.Append(lineTo5);
            path2.Append(lineTo6);

            pathList.Append(path2);

            // End of the lines

            shapeProperties.Append(CreateCustomGeometry(pathList));

            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawGroupBrackets(Rect cmlExtents, double armLength, double lineWidth, string lineColour)
        {
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);

            Point location = new Point(emuLeft, emuTop);
            Size size = new Size(emuWidth, emuHeight);
            location.Offset(OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Left), OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Top));
            Rect boundingBox = new Rect(location, size);
            Int64Value armLengthEmu = OoXmlHelper.ScaleCmlToEmu(armLength);

            emuWidth = (Int64Value)boundingBox.Width;
            emuHeight = (Int64Value)boundingBox.Height;
            emuTop = (Int64Value)boundingBox.Top;
            emuLeft = (Int64Value)boundingBox.Left;

            string shapeName = "Box " + _ooxmlId++;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            pathList.Append(MakeCorner(boundingBox, "TopLeft", armLengthEmu));
            pathList.Append(MakeCorner(boundingBox, "TopRight", armLengthEmu));
            pathList.Append(MakeCorner(boundingBox, "BottomLeft", armLengthEmu));
            pathList.Append(MakeCorner(boundingBox, "BottomRight", armLengthEmu));

            // End of the lines

            shapeProperties.Append(CreateCustomGeometry(pathList));

            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);

            // Local function
            A.Path MakeCorner(Rect bbRect, string corner, double armsSize)
            {
                var path = new A.Path { Width = (Int64Value)bbRect.Width, Height = (Int64Value)bbRect.Height };

                A.Point p0 = new A.Point();
                A.Point p1 = new A.Point();
                A.Point p2 = new A.Point();

                switch (corner)
                {
                    case "TopLeft":
                        p0 = new A.Point
                        {
                            X = armsSize.ToString("0"),
                            Y = "0"
                        };
                        p1 = new A.Point
                        {
                            X = "0",
                            Y = "0"
                        };
                        p2 = new A.Point
                        {
                            X = "0",
                            Y = armsSize.ToString("0")
                        };
                        break;

                    case "TopRight":
                        p0 = new A.Point
                        {
                            X = (bbRect.Width - armsSize).ToString("0"),
                            Y = "0"
                        };
                        p1 = new A.Point
                        {
                            X = bbRect.Width.ToString("0"),
                            Y = "0"
                        };
                        p2 = new A.Point
                        {
                            X = bbRect.Width.ToString("0"),
                            Y = armsSize.ToString("0")
                        };
                        break;

                    case "BottomLeft":
                        p0 = new A.Point
                        {
                            X = "0",
                            Y = (bbRect.Height - armsSize).ToString("0")
                        };
                        p1 = new A.Point
                        {
                            X = "0",
                            Y = bbRect.Height.ToString("0")
                        };
                        p2 = new A.Point
                        {
                            X = armsSize.ToString("0"),
                            Y = bbRect.Height.ToString("0")
                        };
                        break;

                    case "BottomRight":
                        p0 = new A.Point
                        {
                            X = bbRect.Width.ToString("0"),
                            Y = (bbRect.Height - armsSize).ToString("0")
                        };
                        p1 = new A.Point
                        {
                            X = bbRect.Width.ToString("0"),
                            Y = bbRect.Height.ToString("0")
                        };
                        p2 = new A.Point
                        {
                            X = (bbRect.Width - armsSize).ToString("0"),
                            Y = bbRect.Height.ToString("0")
                        };
                        break;
                }

                var moveTo = new A.MoveTo();
                moveTo.Append(p0);
                path.Append(moveTo);

                var lineTo1 = new A.LineTo();
                lineTo1.Append(p1);
                path.Append(lineTo1);

                var lineTo2 = new A.LineTo();
                lineTo2.Append(p2);
                path.Append(lineTo2);

                return path;
            }
        }

        private void DrawBox(Rect cmlExtents,
                             string lineColour = "000000",
                             double lineWidth = OoXmlHelper.ACS_LINE_WIDTH)
        {
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);
            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);

            Point location = new Point(emuLeft, emuTop);
            Size size = new Size(emuWidth, emuHeight);
            location.Offset(OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Left), OoXmlHelper.ScaleCmlToEmu(-_boundingBoxOfEverything.Top));
            Rect boundingBox = new Rect(location, size);

            emuWidth = (Int64Value)boundingBox.Width;
            emuHeight = (Int64Value)boundingBox.Height;
            emuTop = (Int64Value)boundingBox.Top;
            emuLeft = (Int64Value)boundingBox.Left;

            string shapeName = "Box " + _ooxmlId++;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            // Starting Point
            A.MoveTo moveTo = new A.MoveTo();
            A.Point point1 = new A.Point { X = "0", Y = "0" };
            moveTo.Append(point1);

            // Mid Point
            A.LineTo lineTo1 = new A.LineTo();
            A.Point point2 = new A.Point { X = boundingBox.Width.ToString("0"), Y = "0" };
            lineTo1.Append(point2);

            // Mid Point
            A.LineTo lineTo2 = new A.LineTo();
            A.Point point3 = new A.Point { X = boundingBox.Width.ToString("0"), Y = boundingBox.Height.ToString("0") };
            lineTo2.Append(point3);

            // Last Point
            A.LineTo lineTo3 = new A.LineTo();
            A.Point point4 = new A.Point { X = "0", Y = boundingBox.Height.ToString("0") };
            lineTo3.Append(point4);

            // Back to Start Point
            A.LineTo lineTo4 = new A.LineTo();
            A.Point point5 = new A.Point { X = "0", Y = "0" };
            lineTo4.Append(point5);

            path.Append(moveTo);
            path.Append(lineTo1);
            path.Append(lineTo2);
            path.Append(lineTo3);
            path.Append(lineTo4);

            pathList.Append(path);

            // End of the lines

            shapeProperties.Append(CreateCustomGeometry(pathList));

            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawPolygon(List<Point> points, string lineColour, double lineWidth)
        {
            Rect cmlExtents = new Rect(points[0], points[points.Count - 1]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                cmlExtents.Union(new Rect(points[i], points[i + 1]));
            }

            // Move Extents to have 0,0 Top Left Reference
            cmlExtents.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);

            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Left);
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlExtents.Height);

            long id = _ooxmlId++;
            string shapeName = "Polygon " + id;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(id, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            // First point
            A.MoveTo moveTo = new A.MoveTo();
            moveTo.Append(MakePoint(points[0], cmlExtents));
            path.Append(moveTo);

            // Remaining points
            for (int i = 1; i < points.Count; i++)
            {
                A.LineTo lineTo = new A.LineTo();
                lineTo.Append(MakePoint(points[i], cmlExtents));
                path.Append(lineTo);
            }

            // Close the path
            A.CloseShapePath closeShapePath = new A.CloseShapePath();
            path.Append(closeShapePath);

            pathList.Append(path);

            // End of the lines

            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            shapeProperties.Append(CreateCustomGeometry(pathList));
            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawStraightLine(Point bondStart, Point bondEnd, string bondPath, BondLineStyle lineStyle, string lineColour, double lineWidth)
        {
            var tuple = OffsetPoints(bondStart, bondEnd);
            Point cmlStartPoint = tuple.Start;
            Point cmlEndPoint = tuple.End;
            Rect cmlLineExtents = tuple.Extents;

            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(cmlLineExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(cmlLineExtents.Left);
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(cmlLineExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(cmlLineExtents.Height);

            long id = _ooxmlId++;
            string suffix = string.IsNullOrEmpty(bondPath) ? id.ToString() : bondPath;
            string shapeName = "Straight Line " + suffix;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(id, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            A.MoveTo moveTo = new A.MoveTo();
            A.Point point1 = new A.Point { X = OoXmlHelper.ScaleCmlToEmu(cmlStartPoint.X).ToString(), Y = OoXmlHelper.ScaleCmlToEmu(cmlStartPoint.Y).ToString() };
            moveTo.Append(point1);
            path.Append(moveTo);

            A.LineTo lineTo = new A.LineTo();
            A.Point point2 = new A.Point { X = OoXmlHelper.ScaleCmlToEmu(cmlEndPoint.X).ToString(), Y = OoXmlHelper.ScaleCmlToEmu(cmlEndPoint.Y).ToString() };
            lineTo.Append(point2);
            path.Append(lineTo);

            pathList.Append(path);

            // End of the lines

            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            switch (lineStyle)
            {
                case BondLineStyle.Dashed:
                    A.PresetDash dashed = new A.PresetDash { Val = A.PresetLineDashValues.SystemDash };
                    outline.Append(dashed);
                    break;

                case BondLineStyle.Dotted:
                    A.PresetDash dotted = new A.PresetDash { Val = A.PresetLineDashValues.SystemDot };
                    outline.Append(dotted);
                    break;
            }

            if (!string.IsNullOrEmpty(bondPath) && _options.ShowBondDirection)
            {
                A.TailEnd tailEnd = new A.TailEnd
                {
                    Type = A.LineEndValues.Arrow,
                    Width = A.LineEndWidthValues.Small,
                    Length = A.LineEndLengthValues.Small
                };
                outline.Append(tailEnd);
            }

            shapeProperties.Append(CreateCustomGeometry(pathList));
            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private void DrawWavyLine(Point bondStart, Point bondEnd, string bondPath, string lineColour)
        {
            var tuple = OffsetPoints(bondStart, bondEnd);
            Point cmlStartPoint = tuple.Start;
            Point cmlEndPoint = tuple.End;
            Rect cmlLineExtents = tuple.Extents;

            // Calculate wiggles

            Vector bondVector = cmlEndPoint - cmlStartPoint;
            int noOfWiggles = (int)Math.Ceiling(bondVector.Length / BondOffset());
            if (noOfWiggles < 1)
            {
                noOfWiggles = 1;
            }

            double wiggleLength = bondVector.Length / noOfWiggles;

            Vector originalWigglePortion = bondVector;
            originalWigglePortion.Normalize();
            originalWigglePortion *= wiggleLength / 2;

            Matrix toLeft = new Matrix();
            toLeft.Rotate(-60);
            Matrix toRight = new Matrix();
            toRight.Rotate(60);
            Vector leftVector = originalWigglePortion * toLeft;
            Vector rightVector = originalWigglePortion * toRight;

            List<Point> allpoints = new List<Point>();
            List<List<Point>> allTriangles = new List<List<Point>>();
            List<Point> triangle = new List<Point>();

            Point lastPoint = cmlStartPoint;
            triangle.Add(lastPoint);
            allpoints.Add(lastPoint);

            for (int i = 0; i < noOfWiggles; i++)
            {
                Point leftPoint = lastPoint + leftVector;
                triangle.Add(leftPoint);
                allpoints.Add(leftPoint);

                Point midPoint = lastPoint + originalWigglePortion;
                allpoints.Add(midPoint);
                triangle.Add(midPoint);
                allTriangles.Add(triangle);
                triangle = new List<Point>();
                triangle.Add(midPoint);

                Point rightPoint = lastPoint + originalWigglePortion + rightVector;
                allpoints.Add(rightPoint);
                triangle.Add(rightPoint);

                lastPoint += originalWigglePortion * 2;
                triangle.Add(lastPoint);
                allpoints.Add(lastPoint);

                allTriangles.Add(triangle);
                triangle = new List<Point>();
                triangle.Add(lastPoint);
            }

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (Point p in allpoints)
            {
                maxX = Math.Max(p.X + cmlLineExtents.Left, maxX);
                minX = Math.Min(p.X + cmlLineExtents.Left, minX);
                maxY = Math.Max(p.Y + cmlLineExtents.Top, maxY);
                minY = Math.Min(p.Y + cmlLineExtents.Top, minY);
            }

            Rect newExtents = new Rect(minX, minY, maxX - minX, maxY - minY);
            double xOffset = cmlLineExtents.Left - newExtents.Left;
            double yOffset = cmlLineExtents.Top - newExtents.Top;

            Int64Value emuTop = OoXmlHelper.ScaleCmlToEmu(newExtents.Top);
            Int64Value emuLeft = OoXmlHelper.ScaleCmlToEmu(newExtents.Left);
            Int64Value emuWidth = OoXmlHelper.ScaleCmlToEmu(newExtents.Width);
            Int64Value emuHeight = OoXmlHelper.ScaleCmlToEmu(newExtents.Height);

            string shapeName = "Wavy Line " + bondPath;

            Wps.WordprocessingShape wordprocessingShape = CreateShape(_ooxmlId++, shapeName);
            Wps.ShapeProperties shapeProperties = CreateShapeProperties(wordprocessingShape, emuTop, emuLeft, emuWidth, emuHeight);

            // Start of the lines

            A.PathList pathList = new A.PathList();

            A.Path path = new A.Path { Width = emuWidth, Height = emuHeight };

            A.MoveTo moveTo = new A.MoveTo();
            A.Point firstPoint = new A.Point { X = OoXmlHelper.ScaleCmlToEmu(cmlStartPoint.X).ToString(), Y = OoXmlHelper.ScaleCmlToEmu(cmlStartPoint.Y).ToString() };
            moveTo.Append(firstPoint);
            path.Append(moveTo);

            foreach (var tri in allTriangles)
            {
                A.CubicBezierCurveTo cubicBezierCurveTo = new A.CubicBezierCurveTo();
                foreach (var p in tri)
                {
                    A.Point nextPoint = new A.Point { X = OoXmlHelper.ScaleCmlToEmu(p.X + xOffset).ToString(), Y = OoXmlHelper.ScaleCmlToEmu(p.Y + yOffset).ToString() };
                    cubicBezierCurveTo.Append(nextPoint);
                }
                path.Append(cubicBezierCurveTo);
            }

            pathList.Append(path);

            // End of the lines

            double lineWidth = OoXmlHelper.ACS_LINE_WIDTH;
            Int32Value emuLineWidth = (Int32Value)(lineWidth * OoXmlHelper.EMUS_PER_WORD_POINT);
            A.Outline outline = new A.Outline { Width = emuLineWidth, CapType = A.LineCapValues.Round };

            A.SolidFill solidFill = new A.SolidFill();
            A.RgbColorModelHex rgbColorModelHex = new A.RgbColorModelHex { Val = lineColour };
            solidFill.Append(rgbColorModelHex);
            outline.Append(solidFill);

            if (_options.ShowBondDirection)
            {
                A.TailEnd tailEnd = new A.TailEnd { Type = A.LineEndValues.Stealth };
                outline.Append(tailEnd);
            }

            shapeProperties.Append(CreateCustomGeometry(pathList));
            shapeProperties.Append(outline);

            wordprocessingShape.Append(CreateShapeStyle());

            Wps.TextBodyProperties textBodyProperties = new Wps.TextBodyProperties();
            wordprocessingShape.Append(textBodyProperties);

            _wordprocessingGroup.Append(wordprocessingShape);
        }

        private Wps.WordprocessingShape CreateShape(long id, string shapeName)
        {
            UInt32Value id32 = UInt32Value.FromUInt32((uint)id);
            Wps.WordprocessingShape wordprocessingShape = new Wps.WordprocessingShape();
            Wps.NonVisualDrawingProperties nonVisualDrawingProperties = new Wps.NonVisualDrawingProperties { Id = id32, Name = shapeName };
            Wps.NonVisualDrawingShapeProperties nonVisualDrawingShapeProperties = new Wps.NonVisualDrawingShapeProperties();

            wordprocessingShape.Append(nonVisualDrawingProperties);
            wordprocessingShape.Append(nonVisualDrawingShapeProperties);

            return wordprocessingShape;
        }

        private Wps.ShapeProperties CreateShapeProperties(Wps.WordprocessingShape wordprocessingShape, Int64Value emuTop, Int64Value emuLeft, Int64Value emuWidth, Int64Value emuHeight)
        {
            Wps.ShapeProperties shapeProperties = new Wps.ShapeProperties();

            wordprocessingShape.Append(shapeProperties);

            A.Transform2D transform2D = new A.Transform2D();
            A.Offset offset = new A.Offset { X = emuLeft, Y = emuTop };
            A.Extents extents = new A.Extents { Cx = emuWidth, Cy = emuHeight };
            transform2D.Append(offset);
            transform2D.Append(extents);
            shapeProperties.Append(transform2D);

            return shapeProperties;
        }

        private Wps.ShapeStyle CreateShapeStyle()
        {
            Wps.ShapeStyle shapeStyle = new Wps.ShapeStyle();
            A.LineReference lineReference = new A.LineReference { Index = (UInt32Value)0U };
            A.FillReference fillReference = new A.FillReference { Index = (UInt32Value)0U };
            A.EffectReference effectReference = new A.EffectReference { Index = (UInt32Value)0U };
            A.FontReference fontReference = new A.FontReference { Index = A.FontCollectionIndexValues.Minor };

            shapeStyle.Append(lineReference);
            shapeStyle.Append(fillReference);
            shapeStyle.Append(effectReference);
            shapeStyle.Append(fontReference);

            return shapeStyle;
        }

        private A.CustomGeometry CreateCustomGeometry(A.PathList pathList)
        {
            A.CustomGeometry customGeometry = new A.CustomGeometry();
            A.AdjustValueList adjustValueList = new A.AdjustValueList();
            A.Rectangle rectangle = new A.Rectangle { Left = "l", Top = "t", Right = "r", Bottom = "b" };
            customGeometry.Append(adjustValueList);
            customGeometry.Append(rectangle);
            customGeometry.Append(pathList);
            return customGeometry;
        }

        private Run CreateRun()
        {
            Run run = new Run();

            Drawing drawing = new Drawing();
            run.Append(drawing);

            Wp.Inline inline = new Wp.Inline
            {
                DistanceFromTop = (UInt32Value)0U,
                DistanceFromLeft = (UInt32Value)0U,
                DistanceFromBottom = (UInt32Value)0U,
                DistanceFromRight = (UInt32Value)0U
            };
            drawing.Append(inline);

            Int64Value width = OoXmlHelper.ScaleCmlToEmu(_boundingBoxOfEverything.Width);
            Int64Value height = OoXmlHelper.ScaleCmlToEmu(_boundingBoxOfEverything.Height);
            Wp.Extent extent = new Wp.Extent { Cx = width, Cy = height };

            Wp.EffectExtent effectExtent = new Wp.EffectExtent
            {
                TopEdge = 0L,
                LeftEdge = 0L,
                BottomEdge = 0L,
                RightEdge = 0L
            };

            inline.Append(extent);
            inline.Append(effectExtent);

            UInt32Value inlineId = UInt32Value.FromUInt32((uint)_ooxmlId);
            Wp.DocProperties docProperties = new Wp.DocProperties
            {
                Id = inlineId,
                Name = "Chem4Word Structure"
            };

            inline.Append(docProperties);

            A.Graphic graphic = new A.Graphic();
            graphic.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

            inline.Append(graphic);

            A.GraphicData graphicData = new A.GraphicData
            {
                Uri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup"
            };

            graphic.Append(graphicData);

            _wordprocessingGroup = new Wpg.WordprocessingGroup();
            graphicData.Append(_wordprocessingGroup);

            Wpg.NonVisualGroupDrawingShapeProperties nonVisualGroupDrawingShapeProperties = new Wpg.NonVisualGroupDrawingShapeProperties();

            Wpg.GroupShapeProperties groupShapeProperties = new Wpg.GroupShapeProperties();

            A.TransformGroup transformGroup = new A.TransformGroup();
            A.Offset offset = new A.Offset { X = 0L, Y = 0L };
            A.Extents extents = new A.Extents { Cx = width, Cy = height };
            A.ChildOffset childOffset = new A.ChildOffset { X = 0L, Y = 0L };
            A.ChildExtents childExtents = new A.ChildExtents { Cx = width, Cy = height };

            transformGroup.Append(offset);
            transformGroup.Append(extents);
            transformGroup.Append(childOffset);
            transformGroup.Append(childExtents);

            groupShapeProperties.Append(transformGroup);

            _wordprocessingGroup.Append(nonVisualGroupDrawingShapeProperties);
            _wordprocessingGroup.Append(groupShapeProperties);

            return run;
        }

        private (Point Start, Point End, Rect Extents) OffsetPoints(Point start, Point end)
        {
            Point startPoint = new Point(start.X, start.Y);
            Point endPoint = new Point(end.X, end.Y);
            Rect extents = new Rect(startPoint, endPoint);

            // Move Extents and Points to have 0,0 Top Left Reference
            startPoint.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);
            endPoint.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);
            extents.Offset(-_boundingBoxOfEverything.Left, -_boundingBoxOfEverything.Top);

            // Move points into New Extents
            startPoint.Offset(-extents.Left, -extents.Top);
            endPoint.Offset(-extents.Left, -extents.Top);

            // Return a Tuple with the results
            return (Start: startPoint, End: endPoint, Extents: extents);
        }

        private List<Point> CalculateWedgeOutline(BondLine bl)
        {
            BondLine leftBondLine = bl.GetParallel(BondOffset() / 2);
            BondLine rightBondLine = bl.GetParallel(-BondOffset() / 2);

            List<Point> points = new List<Point>();
            points.Add(new Point(bl.Start.X, bl.Start.Y));
            points.Add(new Point(leftBondLine.End.X, leftBondLine.End.Y));
            points.Add(new Point(bl.End.X, bl.End.Y));
            points.Add(new Point(rightBondLine.End.X, rightBondLine.End.Y));

            Point wedgeStart = new Point(bl.Start.X, bl.Start.Y);
            Point wedgeEndLeft = new Point(leftBondLine.End.X, leftBondLine.End.Y);
            Point wedgeEndRight = new Point(rightBondLine.End.X, rightBondLine.End.Y);

            Bond thisBond = bl.Bond;
            Atom endAtom = thisBond.EndAtom;

            // EndAtom == C and Label is ""
            if (endAtom.Element as Element == Globals.PeriodicTable.C
                && thisBond.Rings.Count == 0
                && string.IsNullOrEmpty(endAtom.SymbolText))
            {
                // Has at least one other bond
                if (endAtom.Bonds.Count() > 1)
                {
                    var otherBonds = endAtom.Bonds.Except(new[] { thisBond }).ToList();
                    bool allSingle = true;
                    List<Bond> nonHydrogenBonds = new List<Bond>();
                    foreach (var otherBond in otherBonds)
                    {
                        if (!otherBond.Order.Equals(Globals.OrderSingle))
                        {
                            allSingle = false;
                            //break;
                        }

                        var otherAtom = otherBond.OtherAtom(endAtom);
                        if (otherAtom.Element as Element != Globals.PeriodicTable.H)
                        {
                            nonHydrogenBonds.Add(otherBond);
                        }
                    }

                    // All other bonds are single
                    if (allSingle)
                    {
                        // Determine chamfer shape
                        Vector left = (wedgeEndLeft - wedgeStart) * 2;
                        Point leftEnd = wedgeStart + left;

                        Vector right = (wedgeEndRight - wedgeStart) * 2;
                        Point rightEnd = wedgeStart + right;

                        bool intersect;
                        Point intersection;

                        Vector shortestLeft = left;
                        Vector shortestRight = right;

                        if (otherBonds.Count - nonHydrogenBonds.Count == 1)
                        {
                            otherBonds = nonHydrogenBonds;
                        }

                        if (otherBonds.Count == 1)
                        {
                            Bond bond = otherBonds[0];
                            Atom atom = bond.OtherAtom(endAtom);
                            Vector vv = (endAtom.Position - atom.Position) * 2;
                            Point otherEnd = atom.Position + vv;

                            CoordinateTool.FindIntersection(wedgeStart, leftEnd,
                                                            atom.Position, otherEnd,
                                                            out _, out intersect, out intersection);
                            if (intersect)
                            {
                                Vector v = intersection - wedgeStart;
                                if (v.Length < shortestLeft.Length)
                                {
                                    shortestLeft = v;
                                }
                            }

                            CoordinateTool.FindIntersection(wedgeStart, rightEnd,
                                                            atom.Position, otherEnd,
                                                            out _, out intersect, out intersection);
                            if (intersect)
                            {
                                Vector v = intersection - wedgeStart;
                                if (v.Length < shortestRight.Length)
                                {
                                    shortestRight = v;
                                }
                            }

                            // Re-write list of points
                            points = new List<Point>();
                            points.Add(wedgeStart);
                            points.Add(wedgeStart + shortestLeft);
                            points.Add(endAtom.Position);
                            points.Add(wedgeStart + shortestRight);
                        }
                        else
                        {
                            foreach (var bond in otherBonds)
                            {
                                CoordinateTool.FindIntersection(wedgeStart, leftEnd,
                                                                bond.StartAtom.Position, bond.EndAtom.Position,
                                                                out _, out intersect, out intersection);
                                if (intersect)
                                {
                                    Vector v = intersection - wedgeStart;
                                    if (v.Length < shortestLeft.Length)
                                    {
                                        shortestLeft = v;
                                    }
                                }

                                CoordinateTool.FindIntersection(wedgeStart, rightEnd,
                                                                bond.StartAtom.Position, bond.EndAtom.Position,
                                                                out _, out intersect, out intersection);
                                if (intersect)
                                {
                                    Vector v = intersection - wedgeStart;
                                    if (v.Length < shortestRight.Length)
                                    {
                                        shortestRight = v;
                                    }
                                }
                            }

                            // Re-write list of points
                            points = new List<Point>();
                            points.Add(wedgeStart);
                            points.Add(wedgeStart + shortestLeft);
                            points.Add(endAtom.Position);
                            points.Add(wedgeStart + shortestRight);
                        }
                    }
                }
            }

            return points;
        }

        private void LoadFont()
        {
            string json = ResourceHelper.GetStringResource(Assembly.GetExecutingAssembly(), "Arial.json");
            _TtfCharacterSet = JsonConvert.DeserializeObject<Dictionary<char, TtfCharacter>>(json);

            //foreach (var c in _TtfCharacterSet.Values)
            //{
            //    Debug.WriteLine($"{c.Character},{c.OriginX},{c.Width},{c.IncrementX}");
            //}
            //Debugger.Break();
        }

        /// <summary>
        /// Sets the canvas size to accomodate any extra space required by label characters
        /// </summary>
        private void SetCanvasSize()
        {
            _boundingBoxOfEverything = _boundingBoxOfAllAtoms;

            foreach (AtomLabelCharacter alc in _atomLabelCharacters)
            {
                if (alc.IsSubScript)
                {
                    Rect r = new Rect(alc.Position,
                                      new Size(OoXmlHelper.ScaleCsTtfToCml(alc.Character.Width, _medianBondLength) * OoXmlHelper.SUBSCRIPT_SCALE_FACTOR,
                                               OoXmlHelper.ScaleCsTtfToCml(alc.Character.Height, _medianBondLength) * OoXmlHelper.SUBSCRIPT_SCALE_FACTOR));
                    _boundingBoxOfEverything.Union(r);
                }
                else
                {
                    Rect r = new Rect(alc.Position,
                                      new Size(OoXmlHelper.ScaleCsTtfToCml(alc.Character.Width, _medianBondLength),
                                               OoXmlHelper.ScaleCsTtfToCml(alc.Character.Height, _medianBondLength)));
                    _boundingBoxOfEverything.Union(r);
                }
            }

            foreach (var group in _allMoleculeExtents)
            {
                _boundingBoxOfEverything.Union(group.ExternalCharacterExtents);
            }

            _boundingBoxOfEverything.Inflate(OoXmlHelper.DRAWING_MARGIN, OoXmlHelper.DRAWING_MARGIN);
        }

        private double BondOffset()
        {
            return _medianBondLength * OoXmlHelper.MULTIPLE_BOND_OFFSET_PERCENTAGE;
        }

        private static void ShutDownProgress(Progress pb)
        {
            pb.Value = 0;
            pb.Hide();
            pb.Close();
        }
    }
}