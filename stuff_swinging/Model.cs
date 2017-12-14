using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Diagnostics;

namespace stuff_oscillating
{
    public static class Model
    {

        public class ModelParameters
        {
            public double ObjectMass = 1;
            public double InitialVelocity = 0;
            public double InitialAngle  = 1;
            public double Length = 10;
            public double Shift = 0;
            public double EnviromentDensity = 1.1;
            public double ObjectDensity { get { return ObjectMass / ObjectVolume; } }
            public double ObjectVolume { get { return 4.0 / 3.0 * Math.PI * Math.Pow(Radius, 3); } }
            public bool UseArchimedes = false;
            public bool UseDrag = true;
            public bool UseViscosity = false;
            public double Radius = 1;
            public double EnviromentViscosity = 1;
            public double CrossSectionArea { get { return Math.PI * Math.Pow(Radius, 2); } }
            public double DragCoeff { get { return EnviromentDensity * CrossSectionArea; } }
            public double ViscosityCoeff { get { return 6 * Math.PI * EnviromentViscosity * EnviromentDensity * Radius; } }

            private static ReaderWriterLockSlim fa_rwlock = new ReaderWriterLockSlim();
            private double forceAmplitude = 0;
            public double ShiftAmplitude {
                get
                {
                    try
                    {
                        fa_rwlock.EnterReadLock();
                        return forceAmplitude;
                    }
                    finally
                    {
                        fa_rwlock.ExitReadLock();
                    }
                }
                set
                {
                    try
                    {
                        fa_rwlock.EnterWriteLock();
                        forceAmplitude = value;
                    }
                    finally
                    {
                        fa_rwlock.ExitWriteLock();
                    }
                }
            }

            private static ReaderWriterLockSlim fp_rwlock = new ReaderWriterLockSlim();
            private double forcePeriod = 1;
            public double ShiftPeriod
            {
                get
                {
                    try
                    {
                        fp_rwlock.EnterReadLock();
                        return forcePeriod;
                    }
                    finally
                    {
                        fp_rwlock.ExitReadLock();
                    }
                }
                set
                {
                    try
                    {
                        fp_rwlock.EnterWriteLock();
                        forcePeriod = value;
                    }
                    finally
                    {
                        fp_rwlock.ExitWriteLock();
                    }
                }
            }

        }

        public class ModelStatus : EventArgs
        {
            public double Angle;
            public double Velocity;
            public double Energy;
            public double Time;
        }

        private static ReaderWriterLockSlim imp_rwlock = new ReaderWriterLockSlim();
        private static ReaderWriterLockSlim param_rwlock = new ReaderWriterLockSlim();

        private static Stopwatch stopwatch = new Stopwatch();
        private static Timer timer = null;

        private static ModelStatus modelStatus = new ModelStatus();

        private static double impulse = 0;
        public  static double Impulse
        {
            get
            {
                try
                {
                    imp_rwlock.EnterReadLock();
                    return impulse;
                }
                finally
                {
                    imp_rwlock.ExitReadLock();
                }
            }
            set
            {
                try
                {
                    imp_rwlock.EnterWriteLock();
                    impulse = value;
                }
                finally
                {
                    imp_rwlock.ExitWriteLock();
                }
            }
        }

        private static ModelParameters parameters = new ModelParameters();
        public static ModelParameters Parameters
        {
            get
            {
                try
                {
                    param_rwlock.EnterReadLock();
                    return parameters;
                }
                finally
                {
                    param_rwlock.ExitReadLock();
                }
            }
        }

        private static Func<double, double, double, double> f;

        public static EventHandler<ModelStatus> ModelTick;

        private static void CalculateState(object state)
        {
            double dt = stopwatch.Elapsed.TotalMilliseconds / 1000 - modelStatus.Time;
            double t = modelStatus.Time;
            double a = modelStatus.Angle;
            double v = modelStatus.Velocity;
            double impulse = Impulse / parameters.ObjectMass / parameters.Length;
            Impulse = 0;
            double k11 = f(t, a, v);
            double k21 = v;
            double k12 = f(t + dt / 2, a + dt * k21 / 2, v + dt * k11 / 2);
            double k22 = v + dt * k11 / 2;
            double k13 = f(t + dt / 2, a + dt * k22 / 2, v + dt * k12 / 2);
            double k23 = v + dt * k12 / 2;
            double k14 = f(t + dt, a + dt * k23, v + dt * k13);
            double k24 = v + dt * k13;
            modelStatus.Velocity += dt * (k11 + 2 * k12 + 2 * k13 + k14) / 6 + impulse;
            a += dt * (k21 + 2 * k22 + 2 * k23 + k24) / 6;
            a -= Math.Floor((a + Math.PI) / 2 / Math.PI) * 2 * Math.PI;
            modelStatus.Angle = a;
            modelStatus.Time += dt;
            modelStatus.Energy = parameters.ObjectMass * 9.81 * parameters.Length * (1 - Math.Cos(modelStatus.Angle)) +
                0.5 * parameters.ObjectMass * parameters.Length * parameters.Length * modelStatus.Velocity * modelStatus.Velocity;
            ModelTick(null, modelStatus);
        }

        public static void Pause()
        {
            timer.Change(Timeout.Infinite, 50);
            stopwatch.Stop();
        }

        public static void Resume()
        {
            stopwatch.Start();
            timer.Change(0, 50);
        }

        public static void Start(ModelParameters new_parameters)
        {
            parameters = new_parameters;
            modelStatus = new ModelStatus()
            {
                Time = 0,
                Angle = parameters.InitialAngle,
                Velocity = parameters.InitialVelocity,
                Energy = parameters.ObjectMass * 9.81 * parameters.Length * (1 - Math.Cos(modelStatus.Angle)) +
                0.5 * parameters.ObjectMass * parameters.Length * parameters.Length * modelStatus.Velocity * modelStatus.Velocity
            };
            Func<double, double> nf = a => -9.81 * Math.Sin(a);
            Func<double, double> archimedes = a => 0;
            Func<double, double, double, double> drag = (t, a, v) => 0;
            if (parameters.UseArchimedes)
                archimedes = a => 9.81 * parameters.EnviromentDensity / parameters.ObjectDensity * Math.Sin(a);
            if (parameters.UseViscosity)
                drag = (t, a, v) =>
                    parameters.ViscosityCoeff * (-Math.Sin(a - Math.PI / 2) * 
                        (parameters.Shift + parameters.ShiftAmplitude * Math.Cos(parameters.ShiftPeriod * t)) - v * parameters.Length) / parameters.ObjectMass;
            else if (parameters.UseDrag)
                drag = (t, a, v) =>
                {
                    double av = -Math.Sin(a - Math.PI / 2) *
                        (parameters.Shift + parameters.ShiftAmplitude * Math.Cos(parameters.ShiftPeriod * t)) - v * parameters.Length;
                    return parameters.DragCoeff * Math.Abs(av) * av / parameters.ObjectMass;
                };
            f = (t, a, v) => (nf(a) + archimedes(a) + drag(t, a, v)) / parameters.Length;
            stopwatch.Restart();
            timer = new Timer(CalculateState, null, 0, 50);
        }

        public static void Stop()
        {
            timer.Dispose();
        }

    }
}
