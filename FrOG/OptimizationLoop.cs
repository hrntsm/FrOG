using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace FrOG
{
    internal static class OptimizationLoop
    {
        //Settings
        public static string Solversettings;

        public static bool BolMaximize;
        public static bool BolMaxIter;
        public static int MaxIter;
        public static bool BolMaxIterNoProgress;
        public static int MaxIterNoProgress;
        public static bool BolMaxDuration;
        public static double MaxDuration;
        public static bool BolRuns;
        public static int Runs;
        public static int PresetIndex;
        public static bool BolRandomize = true;

        //Expertsettings for RBFOpt
        public static string ExpertSettings;

        //BolLog Settings
        public static bool BolLog;
        public static string LogName;

        //Variables
        private static BackgroundWorker s_worker;
        private static OptimizationComponent s_component;

        private static int s_iterations;
        private static int s_iterationsCurrentBest;
        private static double s_bestValue;
        private static IList<decimal> s_bestParams;
        private static OptimizationResult.ResultType s_resultType;

        private static readonly Log Log;
        private static LoggerLog s_loggerLog;
        private static Stopwatch s_stopwatchTotal;
        private static Stopwatch s_stopwatchLoop;
        private static double s_stopwatchPreviousMilliseconds;
        private static readonly double UpdateFrequency = 100;
        private static double s_updateElapsed;

        //List of Best Values
        private static readonly List<double> BestValues = new List<double>();

        //Run MultipleOptimizationRuns(Entry point: Run RunOptimizationLoop (more than) once)
        public static void RunOptimizationLoopMultiple(object sender, DoWorkEventArgs e)
        {
            var logBaseName = LogName;
            var bestResult = new OptimizationResult(BolMaximize ? double.NegativeInfinity : double.PositiveInfinity, new List<decimal>(), 0, OptimizationResult.ResultType.Unknown);

            //Get worker and component
            s_worker = sender as BackgroundWorker;
            s_component = (OptimizationComponent)e.Argument;

            if (s_component == null)
            {
                MessageBox.Show("FrOG Component not set to an object", "FrOG Error");
                return;
            }

            //Setup Variables
            s_component.GhInOut_Instantiate();
            if (!s_component.GhInOut.SetInputs() || !s_component.GhInOut.SetOutput())
            {
                MessageBox.Show("Getting Variables and/or Objective failed", "Opossum Error");
                return;
            }
            //MessageBox.Show(_component.GhInOut.VariablesStr, "FrOG Variables");

            //Main Loop
            var finishedRuns = 0;

            while (finishedRuns < Runs)
            {
                //MessageBox.Show(finishedRuns.ToString());
                if (s_worker == null)
                {
                    //MessageBox.Show("worker is null");
                    break;
                }
                if (s_worker.CancellationPending)
                {
                    //MessageBox.Show("worker cancellation pending");
                    break;
                }
                //Log
                if (BolLog) LogName = logBaseName;
                if (BolRuns) LogName += String.Format("_{0}", finishedRuns + 1);

                //Run RBFOpt
                var result = RunOptimizationLoop(s_worker, PresetIndex);

                //Exit if there is no result
                if (result == null)
                {
                    //MessageBox.Show("result is null");
                    break;
                }
                //Check is there is a better result
                if ((!BolMaximize && result.Value < bestResult.Value) || (BolMaximize && result.Value > bestResult.Value))
                    bestResult = result;

                //Very important to keep FrOG from crashing (probably needed to dispose the process in Run)
                System.Threading.Thread.Sleep(1000);

                finishedRuns++;
            }

            //Exit when there is no result
            if (double.IsPositiveInfinity(bestResult.Value) || double.IsNegativeInfinity(bestResult.Value)) return;

            //Set Grasshopper model to best value
            s_component.OptimizationWindow.GrasshopperStatus = OptimizationWindow.GrasshopperStates.RequestSent;
            s_worker.ReportProgress(0, bestResult.Parameters);
            //possible tight looping below... perhaps time limit should be added
            while (s_component.OptimizationWindow.GrasshopperStatus !=
                   OptimizationWindow.GrasshopperStates.RequestProcessed)
            {
                //just wait until the cows come home
            }

            //Show Result Message Box
            if (!BolRuns)
                MessageBox.Show(Log.GetResultString(bestResult, MaxIter, MaxIterNoProgress, MaxDuration), "FrOG Result");
            else
                MessageBox.Show(String.Format("Finished {0} runs" + Environment.NewLine + "Overall best value {1}", finishedRuns, bestResult.Value), "FrOG Result");

            if (s_worker != null) s_worker.CancelAsync();
        }

        //Run Solver (Main function)
        private static OptimizationResult RunOptimizationLoop(BackgroundWorker worker, int presetIndex)
        {
            s_iterations = 0;
            s_iterationsCurrentBest = 0;
            s_bestValue = BolMaximize ? double.NegativeInfinity : double.PositiveInfinity;
            s_bestParams = new List<decimal>();
            s_resultType = OptimizationResult.ResultType.Unknown;

            //Get variables
            var variables = s_component.GhInOut.Variables;
            //MessageBox.Show($"Parameter String: {variables}", "FrOG Parameters");

            //Stopwatches
            s_stopwatchTotal = Stopwatch.StartNew();
            s_stopwatchLoop = Stopwatch.StartNew();
            s_stopwatchTotal.Start();
            s_stopwatchPreviousMilliseconds = UpdateFrequency;

            //Clear Best Value List
            BestValues.Clear();

            //Prepare Solver
            if (worker.CancellationPending) return null;
            var solver = SolverList.GetSolverByIndex(PresetIndex);
            var preset = SolverList.GetPresetByIndex(presetIndex);

            //Prepare Log
            //_log = BolLog ? new Log(String.Format("{0}\\{1}.txt", Path.GetDirectoryName(_ghInOut.DocumentPath), LogName)) : null;
            s_loggerLog = BolLog ? new LoggerLog(String.Format("{0}\\{1}.txt", Path.GetDirectoryName(s_component.GhInOut.DocumentPath), LogName)) : null;


            //Log Settings
            if (Log != null) Log.LogSettings(preset);
            //_log?.LogSettings(preset);

            //Run Solver
            //MessageBox.Show("Starting Solver", "FrOG Debug");
            var bolSolverStarted = solver.RunSolver(variables, EvaluateFunction, preset, Solversettings, s_component.GhInOut.ComponentFolder, s_component.GhInOut.DocumentPath);

            if (!bolSolverStarted)
            {
                MessageBox.Show("Solver could not be started!");
                return null;
            }

            //Show Messagebox with FrOG error
            if (worker.CancellationPending)
                s_resultType = OptimizationResult.ResultType.UserStopped;
            else if (s_resultType == OptimizationResult.ResultType.SolverStopped || s_resultType == OptimizationResult.ResultType.Unknown)
            {
                var strError = solver.GetErrorMessage();
                if (!string.IsNullOrEmpty(strError)) MessageBox.Show(strError, "FrOG Error");
            }

            //Result
            s_stopwatchLoop.Stop();
            s_stopwatchTotal.Stop();

            var result = new OptimizationResult(s_bestValue, s_bestParams, s_iterations, s_resultType);
            if (Log != null) Log.LogResult(result, s_stopwatchTotal, MaxIter, MaxIterNoProgress, MaxDuration);
            //_log?.LogResult(result, _stopwatchTotal, MaxIter, MaxIterNoProgress, MaxDuration);

            return result;
        }

        public static double EvaluateFunction(IList<decimal> values)
        {
            if (Log != null) Log.LogIteration(s_iterations + 1);
            //_log?.LogIteration(_iterations + 1);
            //var strMessage = "Iteration " + _iterations + Environment.NewLine;
            //strMessage += $"Maximize: {BolMaximize}" + Environment.NewLine;
            //MessageBox.Show(strMessage);
            //MessageBox.Show("Variable Values: " + string.Join(" ",values));

            if (values == null)
            {
                s_resultType = OptimizationResult.ResultType.SolverStopped;
                return double.NaN;
            }

            //Log Parameters
            if (Log != null) Log.LogParameters(string.Join(",", values), s_stopwatchLoop);
            //_log?.LogParameters(string.Join(",", values), _stopwatchLoop);

            s_updateElapsed = s_stopwatchTotal.ElapsedMilliseconds - s_stopwatchPreviousMilliseconds;
            s_stopwatchLoop.Reset();
            s_stopwatchLoop.Start();

            //Run a new solution
            if (s_worker.CancellationPending) return double.NaN;

            s_component.OptimizationWindow.GrasshopperStatus = OptimizationWindow.GrasshopperStates.RequestSent;
            //Call component to recalculate Grasshopper
            s_worker.ReportProgress(0, values);
            //should add a time limit for this to break loop
            while (s_component.OptimizationWindow.GrasshopperStatus != OptimizationWindow.GrasshopperStates.RequestProcessed) { /*just wait*/ }

            //Evaluate Function
            var objectiveValue = s_component.GhInOut.GetObjectiveValue();
            if (double.IsNaN(objectiveValue))
            {
                s_resultType = OptimizationResult.ResultType.FrogStopped;
                return double.NaN;
            }

            s_stopwatchLoop.Stop();

            //MessageBox.Show($"Function value: {objectiveValue}");

            //BolLog Solution
            if (Log != null) Log.LogFunctionValue(objectiveValue, s_stopwatchLoop);
            if (s_loggerLog != null) s_loggerLog.LogLoggerLine(s_component.GhInOut.DocumentName, string.Join(",", values), objectiveValue);
            //_log?.LogFunctionValue(objectiveValue, _stopwatchLoop);

            s_iterations += 1;
            s_iterationsCurrentBest += 1;

            //Keep track of best value
            if ((!BolMaximize && objectiveValue < s_bestValue) || (BolMaximize && objectiveValue > s_bestValue))
            {
                s_bestValue = objectiveValue;
                s_bestParams = values;
                s_iterationsCurrentBest = 0;
            }

            BestValues.Add(s_bestValue);

            //Report Best Values and Redraw Chart
            //only if the elapsed time from stopwatch is larger than the frequency set. (prevents hanging the GUI for OpossumWindow)

            Debug.WriteLine($"Elapsed {s_updateElapsed} Limit {UpdateFrequency}");

            if (s_updateElapsed > UpdateFrequency)
            {
                s_worker.ReportProgress(100, BestValues);
                s_updateElapsed = 0;
                s_stopwatchPreviousMilliseconds = s_stopwatchTotal.ElapsedMilliseconds;
                Debug.WriteLine($"Progress reported, Elapsed {s_updateElapsed}");
            }

            //BolLog Minimum
            if (Log != null) Log.LogCurrentBest(s_bestParams, s_bestValue, s_stopwatchTotal, s_iterationsCurrentBest);
            //_log?.LogCurrentBest(_bestParams, _bestValue, _stopwatchTotal, _iterationsCurrentBest);

            //Optimization Results
            //No Improvement
            if (BolMaxIterNoProgress && s_iterationsCurrentBest >= MaxIterNoProgress)
            {
                s_resultType = OptimizationResult.ResultType.NoImprovement;
                return double.NaN;
            }
            //Maximum Evaluations reached
            if (BolMaxIter && s_iterations >= MaxIter)
            {
                //_worker.CancelAsync();
                s_resultType = OptimizationResult.ResultType.MaximumEvals;
                return double.NaN;
            }
            //Maximum Duration reached
            if (BolMaxDuration && s_stopwatchTotal.Elapsed.TotalSeconds >= MaxDuration)
            {
                //_worker.CancelAsync();
                s_resultType = OptimizationResult.ResultType.MaximumTime;
                return double.NaN;
            }
            //Else: Pass result to RBFOpt
            s_stopwatchLoop.Reset();
            s_stopwatchLoop.Start();

            if (BolMaximize) objectiveValue = -objectiveValue;
            return objectiveValue;
        }
    }
}