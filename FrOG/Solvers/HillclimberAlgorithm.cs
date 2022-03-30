using System;

namespace FrOG.Solvers
{
    public class HillclimberAlgorithm
    {
        //source: Stochastic Hill-Climbing, in: Clever Algorithms: Nature-Inspired Programming Recipes (Jason Brownlee)
        //
        //Input: Itermax, ProblemSize 
        //Output: Current 
        //Current  <- RandomSolution(ProblemSize)
        //For (iteri ∈ Itermax )
        //    Candidate  <- RandomNeighbor(Current)
        //    If (Cost(Candidate) >= Cost(Current))
        //        Current  <- Candidate
        //    End
        //End
        //Return (Current)

        /// <summary>
        /// Lower bound for each variable.
        /// </summary>
        public double[] Lb { get; private set; }
        /// <summary>
        /// Upper bound for each variable.
        /// </summary>
        public double[] Ub { get; private set; }
        /// <summary>
        /// Stepsize.
        /// </summary>
        public double Stepsize { get; private set; }
        /// <summary>
        /// Maximum iterations.
        /// </summary>
        public int Itermax { get; private set; }
        /// <summary>
        /// Evaluation function.
        /// </summary>
        public Func<double[], double> Evalfnc { get; private set; }
        /// <summary>
        /// Variable vector of final solution.
        /// </summary>
        public double[] Xopt { get; private set; }
        /// <summary>
        /// Cost of final solution.
        /// </summary>
        public double Fxopt { get; private set; }

        private readonly RandomDistributions _rnd;

        /// <summary>
        /// Initialize a stochastic hill climber optimization algorithm. Assuming minimization problems.
        /// </summary>
        /// <param name="lb">Lower bound for each variable.</param>
        /// <param name="ub">Upper bound for each variable.</param>
        /// <param name="stepsize">Stepsize.</param>
        /// <param name="itermax">Maximum iterations.</param>
        /// <param name="evalfnc">Evaluation function.</param>
        /// <param name="seed">Seed for random number generator.</param>
        public HillclimberAlgorithm(double[] lb, double[] ub, double stepsize, int itermax, Func<double[], double> evalfnc, int seed)
        {
            this.Lb = lb;
            this.Ub = ub;
            this.Stepsize = stepsize;
            this.Itermax = itermax;
            this.Evalfnc = evalfnc;

            this._rnd = new RandomDistributions(seed);
        }

        /// <summary>
        /// Minimizes an evaluation function using stochastic hill climbing.
        /// </summary>
        public void Solve()
        {
            int n = Lb.Length;
            double[] x = new double[n];
            double[] stdev = new double[n];

            for (int i = 0; i < n; i++)
            {
                x[i] = _rnd.NextDouble() * (Ub[i] - Lb[i]) + Lb[i];
                stdev[i] = Stepsize * (Ub[i] - Lb[i]);
            }
            double fx = Evalfnc(x);

            for (int t = 0; t < Itermax; t++)
            {
                double[] xtest = new double[n];
                for (int i = 0; i < n; i++)
                {
                    xtest[i] = _rnd.NextGaussian(x[i], stdev[i]);
                    if (xtest[i] > Ub[i]) xtest[i] = Ub[i];
                    else if (xtest[i] < Lb[i]) xtest[i] = Lb[i];
                }
                double fxtest = Evalfnc(xtest);

                if (double.IsNaN(fxtest)) return;

                if (fxtest < fx)
                {
                    xtest.CopyTo(x, 0);
                    fx = fxtest;

                    Xopt = new double[n];
                    x.CopyTo(Xopt, 0);
                    Fxopt = fx;
                }
            }
        }

        /// <summary>
        /// Get the variable vector of the final solution.
        /// </summary>
        /// <returns>Variable vector.</returns>
        public double[] Get_Xoptimum()
        {
            return this.Xopt;
        }

        /// <summary>
        /// Get the cost value of the final solution.
        /// </summary>
        /// <returns>Cost value.</returns>
        public double Get_fxoptimum()
        {
            return this.Fxopt;
        }
    }

    public class RandomDistributions : Random
    {
        public RandomDistributions(int rndSeed)
            : base(rndSeed)
        { }

        /// <summary>
        /// Normal distributed random number.
        /// </summary>
        /// <param name="mean">Mean of the distribution.</param>
        /// <param name="stdDev">Standard deviation of the distribution.</param>
        /// <returns>Normal distributed random number.</returns>
        public double NextGaussian(double mean, double stdDev)
        {
            //Random rand = new Random(); //reuse this if you are generating many
            double u1 = base.NextDouble(); //these are uniform(0,1) random doubles
            double u2 = base.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                         Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            double randNormal =
                         mean + stdDev * randStdNormal; //random normal(mean,stdDev^2)

            return randNormal;

        }


    }
}