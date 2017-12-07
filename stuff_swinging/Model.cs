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
            public double RestrictionCoeffitient = 1;
            public double FrictionCoeffitient = 0.1;
#pragma warning disable IDE1006 // Стили именования
            public double ω => RestrictionCoeffitient / ObjectMass;
#pragma warning restore IDE1006 // Стили именования
            public double InitialVelocity = 0;
            public double InitialX  = 1;
            public double Dt = 0.5;
            public double Length = 10;

            private static ReaderWriterLockSlim fa_rwlock = new ReaderWriterLockSlim();
            private double forceAmplitude = 0;
            public double ForceAmplitude {
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
            public double ForcePeriod
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

            private static ReaderWriterLockSlim rwlock = new ReaderWriterLockSlim();
            private bool useForce = false;
            public bool UseForce
            {
                get
                {
                    try
                    {
                        rwlock.EnterReadLock();
                        return useForce;
                    }
                    finally
                    {
                        rwlock.ExitReadLock();
                    }
                }
                set
                {
                    try
                    {
                        rwlock.EnterWriteLock();
                        useForce = value;
                    }
                    finally
                    {
                        rwlock.ExitWriteLock();
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

        private static double F(double t, double y, double p)
        {
            return -9.81 / parameters.Length * Math.Sin(y);
        }

        public static EventHandler<ModelStatus> ModelTick;

        private static void CalculateState(object state)
        {
            double dt = stopwatch.Elapsed.TotalMilliseconds / 1000 - modelStatus.Time;
            double t = modelStatus.Time;
            double a = modelStatus.Angle;
            double v = modelStatus.Velocity;
            double k11 = F(t, a, v);
            double k21 = v;
            double k12 = F(t + dt / 2, a + dt * k21 / 2, v + dt * k11 / 2);
            double k22 = v + dt * k11 / 2;
            double k13 = F(t + dt / 2, a + dt * k22 / 2, v + dt * k12 / 2);
            double k23 = v + dt * k12 / 2;
            double k14 = F(t + dt, a + dt * k23, v + dt * k13);
            double k24 = v + dt * k13;
            modelStatus.Velocity += dt * (k11 + 2 * k12 + 2 * k13 + k14) / 6;
            modelStatus.Angle += dt * (k21 + 2 * k22 + 2 * k23 + k24) / 6;
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
            stopwatch.Restart();
            timer = new Timer(CalculateState, null, 0, 50);
            parameters = new_parameters;
            modelStatus = new ModelStatus()
            {
                Time = 0,
                Angle = parameters.InitialX,
                Velocity = parameters.InitialVelocity,
                Energy = 0.5 * (parameters.ObjectMass * parameters.InitialVelocity * parameters.InitialVelocity +
                    parameters.InitialX * parameters.InitialX * parameters.RestrictionCoeffitient)
            };
        }

        public static void Stop()
        {
            timer.Dispose();
        }

    }
}
