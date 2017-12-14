using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using LiveCharts;
using LiveCharts.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.IO;
using CsvHelper;
using SciChart.Charting.Model.DataSeries;
using System.Timers;
using SciChart.Core.Extensions;
using SciChart.Core.Framework;

namespace stuff_oscillating
{ 
    
    public partial class MainWindow : MetroWindow, INotifyPropertyChanged
    {
        static bool IsFirst = true;
        XyDataSeries<double, double> XDataSeries = new XyDataSeries<double, double>() { FifoCapacity = 500, SeriesName = "Угол" };
        XyDataSeries<double, double> SpeedDataSeries = new XyDataSeries<double, double>() { FifoCapacity = 500, SeriesName = "Угловая скорость" };
        XyDataSeries<double, double> EnergyDataSeries = new XyDataSeries<double, double>() { FifoCapacity = 500, SeriesName = "Полная энергия" };
        XyDataSeries<double, double> PhaseDataSeries = new XyDataSeries<double, double>() { FifoCapacity = 500, SeriesName = "Phase", AcceptsUnsortedData=true };
        IUpdateSuspender chartSuspender = null;
        IUpdateSuspender phaseSuspender = null;
        Ellipse ellipse = new Ellipse()
        {
            Fill = new SolidColorBrush(Colors.Teal),
            Width = 50,
            Height = 50
        };
        Line spring = new Line()
        {
            Stroke = new SolidColorBrush(Colors.White),
            X1 = 0,
            X2 = 0,
            Y1 = 0,
            Y2 = 225
        };
        double min = Double.PositiveInfinity;
        double max = Double.NegativeInfinity;

        public MainWindow()
        {
            InitializeComponent();
            if (IsFirst)
            {
                IsFirst = false;
                Closed += OnMainWindowClosed;
            }
            else
                IsCloseButtonEnabled = false;
            xSeries.DataSeries = XDataSeries;
            speedSeries.DataSeries = SpeedDataSeries;
            energySeries.DataSeries = EnergyDataSeries;
            phaseSeries.DataSeries = PhaseDataSeries;
            animCanvas.Children.Add(spring);
            animCanvas.Children.Add(ellipse);
            Canvas.SetLeft(ellipse, 610);
            Canvas.SetTop(ellipse, 550);
            Canvas.SetLeft(spring, 635);
            Canvas.SetTop(spring, 350);
            DataContext = this;
            Model.ModelTick += OnModelTick;
        }

        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private delegate void UpdateDataDelegate(Model.ModelStatus result);

        void UpdateData(Model.ModelStatus result)
        {
            Data.Add(new DataPoint()
            {
                PointNumber = Data.IsEmpty() ? 1 : Data.Last().PointNumber + 1,
                TimePoint = result.Time,
                X = result.Angle,
                V = result.Velocity,
                E = result.Energy,
            });
            using (sciChartSurface.SuspendUpdates())
            {
                XDataSeries.Append(result.Time, result.Angle);
                SpeedDataSeries.Append(result.Time, result.Velocity);
                EnergyDataSeries.Append(result.Time, result.Energy);
            }
            using (sciPhaseChartSurface.SuspendUpdates())
            {
                PhaseDataSeries.Append(result.Angle, result.Velocity);
            }
            if (animTab.IsSelected)
            {
                double l = Model.Parameters.Length;
                double x = -Math.Cos(result.Angle + Math.PI / 2) * 200;
                double y = Math.Sin(result.Angle + Math.PI / 2) * 200;
                Canvas.SetLeft(ellipse, x + 610);
                Canvas.SetTop(ellipse, y + 350 - 25);
                spring.X2 = x;
                spring.Y2 = y;
            }
        }

        void OnModelTick(object sender, Model.ModelStatus result)
        {
            Dispatcher.Invoke(new UpdateDataDelegate(UpdateData), result);
        }

        public ObservableCollection<DataPoint> Data { get; set; } = new ObservableCollection<DataPoint>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void DoubleTBPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            System.Globalization.CultureInfo ci = System.Threading.Thread.CurrentThread.CurrentCulture;
            string decimalSeparator = ci.NumberFormat.NumberDecimalSeparator;
            if (decimalSeparator == ".")
            {
                decimalSeparator = "\\" + decimalSeparator;
            }
            var textBox = sender as TextBox;
            var pos = textBox.CaretIndex;
            e.Handled = !Regex.IsMatch(textBox.Text.Substring(0, pos) + e.Text + textBox.Text.Substring(pos), @"^[-+]?[0-9]*" + decimalSeparator + @"?[0-9]*$");
        }

        private void TB_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = e.Key == Key.Space;
        }

        private String PassDefaultIfEmpty(String s)
        {
            if (String.IsNullOrEmpty(s))
                return "1";
            if (s == "-" || s == "+")
                return s + "1";
            return s;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                StreamWriter file = new StreamWriter(saveFileDialog.FileName);
                var csv = new CsvWriter(file);
                foreach(var item in Data)
                {
                    csv.WriteField(item.PointNumber);
                    csv.WriteField(item.TimePoint);
                    csv.WriteField(item.X);
                    csv.WriteField(item.V);
                    csv.WriteField(item.E);
                    csv.NextRecord();
                }
                file.Close();
                file.Dispose();
            }
        }

        private void TabablzControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlotTab.IsSelected)
            {
                if (chartSuspender != null)
                {
                    chartSuspender.Dispose();
                    chartSuspender = null;
                }
            }
            else
            {
                if (chartSuspender == null)
                    chartSuspender = sciChartSurface.SuspendUpdates();
            }
            if (PhaseTab.IsSelected)
            {
                if (phaseSuspender != null)
                {
                    phaseSuspender.Dispose();
                    phaseSuspender = null;
                }
            }
            else
            {
                if (phaseSuspender == null)
                    phaseSuspender = sciPhaseChartSurface.SuspendUpdates();
            }
        }

        private void StartBtn_OnClick(object sender, RoutedEventArgs e)
        {
            XDataSeries.Clear();
            SpeedDataSeries.Clear();
            EnergyDataSeries.Clear();
            PhaseDataSeries.Clear();
            Data.Clear();
            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            ImpulseBtn.IsEnabled = true;
            PauseButton.IsEnabled = true;
            SaveButton.IsEnabled = false;
            ToggleTextBoxes(false);
            min = Double.PositiveInfinity;
            max = Double.NegativeInfinity;
            Model.Start(new Model.ModelParameters
            {
                ObjectMass = Convert.ToDouble(PassDefaultIfEmpty(MassTB.Text)),
                InitialVelocity = Convert.ToDouble(PassDefaultIfEmpty(InitialSpeedTB.Text)),
                InitialAngle = Convert.ToDouble(PassDefaultIfEmpty(InitialAngleTB.Text)),
                Length = Convert.ToDouble(PassDefaultIfEmpty(LengthTB.Text)),
                Shift = Convert.ToDouble(PassDefaultIfEmpty(ConstantWindTB.Text)),
                EnviromentDensity = Convert.ToDouble(PassDefaultIfEmpty(EnviromentDensityTB.Text)),
                EnviromentViscosity = Convert.ToDouble(PassDefaultIfEmpty(EnviromentViscosityTB.Text)),
                Radius = Convert.ToDouble(PassDefaultIfEmpty(RadiusTB.Text)),
                ShiftAmplitude = Convert.ToDouble(PassDefaultIfEmpty(WindAmplitudeTB.Text)),
                ShiftPeriod = Convert.ToDouble(PassDefaultIfEmpty(WindPeriodTB.Text)),
                UseArchimedes = ArchimedesForceCB.IsChecked.Value,
                UseDrag = GasDragForceCB.IsChecked.Value,
                UseViscosity = LiquidDragForceCB.IsChecked.Value
            });
        }

        private void StopBtn_OnClick(object sender, RoutedEventArgs e)
        {
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            ImpulseBtn.IsEnabled = false;
            PauseButton.IsEnabled = false;
            ResumeButton.IsEnabled = false;
            SaveButton.IsEnabled = true;
            ToggleTextBoxes(true);
            Model.Stop();
        }

        private void ImpulseBtn_OnClick(object sender, RoutedEventArgs e) => Model.Impulse = Convert.ToDouble(ImpulseTB.Text);

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            min = (double)XDataSeries.YMin;
            max = (double)XDataSeries.YMax;
        }

        private void ExternalForceAmplitudeTB_TextChanged(object sender, TextChangedEventArgs e) => Model.Parameters.ShiftAmplitude = Convert.ToDouble(PassDefaultIfEmpty(WindAmplitudeTB.Text));

        private void ExternalForcePeriodTB_TextChanged(object sender, TextChangedEventArgs e) => Model.Parameters.ShiftPeriod = Convert.ToDouble(PassDefaultIfEmpty(WindPeriodTB.Text));

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            PauseButton.IsEnabled = false;
            ResumeButton.IsEnabled = true;
            ImpulseBtn.IsEnabled = false;
            SaveButton.IsEnabled = true;
            Model.Pause();
        }

        private void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            ResumeButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            ImpulseBtn.IsEnabled = true;
            SaveButton.IsEnabled = false;
            Model.Resume();
        }

        private void ToggleTextBoxes(bool value)
        {
            MassTB.IsEnabled = value;
            RadiusTB.IsEnabled = value;
            LengthTB.IsEnabled = value;
            InitialSpeedTB.IsEnabled = value;
            InitialAngleTB.IsEnabled = value;
            WindAmplitudeTB.IsEnabled = value;
            WindPeriodTB.IsEnabled = value;
            ConstantWindTB.IsEnabled = value;
            EnviromentDensityTB.IsEnabled = value;
            EnviromentViscosityTB.IsEnabled = value;
            ArchimedesForceCB.IsEnabled = value;
            GasDragForceCB.IsEnabled = value;
            LiquidDragForceCB.IsEnabled = value;
        }

    }

    public class DataPoint
    {
        public int PointNumber { get; set; }
        public double TimePoint { get; set; }
        public double X { get; set; }
        public double V { get; set; }
        public double E { get; set; }
    }

    [ValueConversion(typeof(object), typeof(string))]
    public class StringFormatConverter : IValueConverter, IMultiValueConverter
    {
        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(new object[] { value }, targetType, parameter, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            System.Diagnostics.Trace.TraceError("StringFormatConverter: does not support TwoWay or OneWayToSource bindings.");
            return DependencyProperty.UnsetValue;
        }

        public virtual object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                string format = parameter?.ToString();
                if (String.IsNullOrEmpty(format))
                {
                    System.Text.StringBuilder builder = new System.Text.StringBuilder();
                    for (int index = 0; index < values.Length; ++index)
                    {
                        builder.Append("{" + index + "}");
                    }
                    format = builder.ToString();
                }
                return String.Format(/*culture,*/ format, values);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("StringFormatConverter({0}): {1}", parameter, ex.Message);
                return DependencyProperty.UnsetValue;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            System.Diagnostics.Trace.TraceError("StringFormatConverter: does not support TwoWay or OneWayToSource bindings.");
            return null;
        }

    }

    [ValueConversion(typeof(object), typeof(string))]
    public class RadiusMassToDensityConverter : StringFormatConverter
    {
        public override object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double r;
            try
            {
                r = Double.Parse(values[0].ToString());
            }
            catch (FormatException e)
            {
                r = 1;
            }
            double v = Math.Pow(r, 3) * Math.PI * 4.0 / 3.0;
            double m;
            try
            {
                m = Double.Parse(values[1].ToString());
            }
            catch (FormatException e)
            {
                m = 1;
            }
            return base.Convert(new object[] { m / v }, targetType, parameter, culture);
        }
    }

    [ValueConversion(typeof(object), typeof(string))]
    public class LengthToPeriodConverter : StringFormatConverter
    {
        public override object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double l;
            try
            {
                l = Double.Parse(values[0].ToString());
            }
            catch (FormatException e)
            {
                l = 1;
            }
            return base.Convert(new object[] { 9.81 / l }, targetType, parameter, culture);
        }
    }

}