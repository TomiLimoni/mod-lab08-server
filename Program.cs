using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScottPlot;

namespace Lab08
{
    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }
    class Server
    {
        private PoolRecord[] pool;
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;

        private double serviceRate;
        private double totalBusyTime = 0;
        private double totalIdleTime = 0;
        private int activeChannels = 0;
        private DateTime lastStateChange;
        private Random rand = new Random();
        public Server(int channels, double serviceRate)
        {
            pool = new PoolRecord[channels];
            this.serviceRate = serviceRate;
            lastStateChange = DateTime.Now;
        }
        public void proc(object? sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        processedCount++;
                        UpdateChannelCount(1);
                        return;
                    }
                }
                rejectedCount++;
            }
        }
        private void Answer(object? arg)
        {
            int id = (int)arg!;
            double serviceTimeSec = ExponentialRandom(1.0 / serviceRate);
            Thread.Sleep((int)(serviceTimeSec * 1000));

            lock (threadLock)
            {
                totalBusyTime += serviceTimeSec;
                for (int i = 0; i < pool.Length; i++)
                    if (pool[i].thread == Thread.CurrentThread)
                        pool[i].in_use = false;
                UpdateChannelCount(-1);
            }
        }
        private void UpdateChannelCount(int delta)
        {
            DateTime now = DateTime.Now;
            double elapsed = (now - lastStateChange).TotalSeconds;
            lastStateChange = now;

            if (activeChannels == 0)
            {
                totalIdleTime += elapsed;
            }
            activeChannels += delta;
        }
        public void FinalizeIdleTime()
        {
            lock (threadLock)
            {
                DateTime now = DateTime.Now;
                double elapsed = (now - lastStateChange).TotalSeconds;
                if (activeChannels == 0)
                {
                    totalIdleTime += elapsed;
                }
                lastStateChange = now;
            }
        }
        private double ExponentialRandom(double mean)
        {
            double u = rand.NextDouble();
            return -mean * Math.Log(1 - u);
        }
        public void WaitForCompletion()
        {
            while (true)
            {
                lock (threadLock)
                {
                    if (activeChannels == 0) break;
                }
                Thread.Sleep(10);
            }
        }
        public double TotalBusyTime => totalBusyTime;
        public double TotalIdleTime => totalIdleTime;
        public double TotalTime => totalBusyTime + totalIdleTime;
    }
    public class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }

    class Client
    {
        private Server server;
        public Client(Server server)
        {
            this.server = server;
            this.request += server.proc;
        }
        public void send(int id)
        {
            procEventArgs args = new procEventArgs();
            args.id = id;
            OnProc(args);
        }
        protected virtual void OnProc(procEventArgs e)
        {
            EventHandler<procEventArgs>? handler = request;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        public event EventHandler<procEventArgs>? request;
    }
    class Program
    {
        static void Main(string[] args)
        {
            int channels = 5;
            double mu = 5.0;
            int requestsTotal = 100;

            double[] lambdaValues = { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 };

            string resultDir = "result";
            if (!Directory.Exists(resultDir))
                Directory.CreateDirectory(resultDir);

            var results = new List<(double lambda,
                double P0_exp, double Pn_exp, double Q_exp, double A_exp, double k_exp,
                double P0_th, double Pn_th, double Q_th, double A_th, double k_th)>();

            Console.WriteLine("Запуск экспериментов");
            foreach (double lambda in lambdaValues)
            {
                Console.Write($"Лямбда = {lambda:F2}: ");
                var sim = RunSingleExperiment(channels, mu, lambda, requestsTotal);
                results.Add(sim);
                Console.WriteLine($" Pn(эксп) = {sim.Pn_exp:F4}, Pn(теор) = {sim.Pn_th:F4}");
            }
            string resultsFile = Path.Combine(resultDir, "output.txt");
            using (StreamWriter writer = new StreamWriter(resultsFile, false, Encoding.UTF8))
            {
                writer.WriteLine("lambda\tP0_exp\tPn_exp\tQ_exp\tA_exp\tk_exp\tP0_th\tPn_th\tQ_th\tA_th\tk_th");
                foreach (var r in results)
                {
                    writer.WriteLine($"{r.lambda:F2}\t{r.P0_exp:F3}\t{r.Pn_exp:F3}\t{r.Q_exp:F3}\t{r.A_exp:F3}\t" +
                        $"{r.k_exp:F3}\t{r.P0_th:F3}\t{r.Pn_th:F3}\t{r.Q_th:F3}\t{r.A_th:F3}\t{r.k_th:F3}");
                }
            }

            double[] lambdas = results.Select(r => r.lambda).ToArray();
            double[] p0_exp = results.Select(r => r.P0_exp).ToArray();
            double[] p0_th = results.Select(r => r.P0_th).ToArray();
            double[] pn_exp = results.Select(r => r.Pn_exp).ToArray();
            double[] pn_th = results.Select(r => r.Pn_th).ToArray();
            double[] q_exp = results.Select(r => r.Q_exp).ToArray();
            double[] q_th = results.Select(r => r.Q_th).ToArray();
            double[] a_exp = results.Select(r => r.A_exp).ToArray();
            double[] a_th = results.Select(r => r.A_th).ToArray();
            double[] k_exp = results.Select(r => r.k_exp).ToArray();
            double[] k_th = results.Select(r => r.k_th).ToArray();

            CreatePlot(lambdas, p0_exp, p0_th, "Вероятность", "Вероятность простоя (P0)", Path.Combine(resultDir, "p-1.png"), ScottPlot.Alignment.LowerLeft);
            CreatePlot(lambdas, pn_exp, pn_th, "Вероятность", "Вероятность отказа (Pn)", Path.Combine(resultDir, "p-2.png"));
            CreatePlot(lambdas, q_exp, q_th, "Q", "Относительная пропускная способность", Path.Combine(resultDir, "p-3.png"), ScottPlot.Alignment.LowerLeft);
            CreatePlot(lambdas, a_exp, a_th, "A (заявок/с)", "Абсолютная пропускная способность", Path.Combine(resultDir, "p-4.png"));
            CreatePlot(lambdas, k_exp, k_th, "k", "Среднее число занятых каналов", Path.Combine(resultDir, "p-5.png"));

            Console.WriteLine("\nГрафики сохранены в папку 'result'.");
            Console.ReadKey();
        }

        static (double lambda,
                double P0_exp, double Pn_exp, double Q_exp, double A_exp, double k_exp,
                double P0_th, double Pn_th, double Q_th, double A_th, double k_th)
            RunSingleExperiment(int n, double mu, double lambda, int totalRequests)
        {
            var server = new Server(n, mu);
            var client = new Client(server);
            Random rand = new Random();

            DateTime startTime = DateTime.Now;

            for (int id = 1; id <= totalRequests; id++)
            {
                double interarrivalSec = -Math.Log(1 - rand.NextDouble()) / lambda;
                int sleepMs = (int)(interarrivalSec * 1000);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
                client.send(id);
            }

            server.WaitForCompletion();
            server.FinalizeIdleTime();

            DateTime endTime = DateTime.Now;
            double totalTime = (endTime - startTime).TotalSeconds;

            double Pn_exp = (double)server.rejectedCount / server.requestCount;
            double Q_exp = 1 - Pn_exp;
            double A_exp = server.processedCount / totalTime;
            double k_exp = server.TotalBusyTime / totalTime;
            double P0_exp = server.TotalIdleTime / totalTime;

            double rho = lambda / mu;
            double sum = 0;
            for (int i = 0; i <= n; i++) sum += Math.Pow(rho, i) / Factorial(i);
            double P0_th = 1.0 / sum;
            double Pn_th = Math.Pow(rho, n) / Factorial(n) * P0_th;
            double Q_th = 1 - Pn_th;
            double A_th = lambda * Q_th;
            double k_th = rho * Q_th;

            return (lambda, P0_exp, Pn_exp, Q_exp, A_exp, k_exp,
                    P0_th, Pn_th, Q_th, A_th, k_th);
        }
        static double Factorial(int n)
        {
            double f = 1;
            for (int i = 2; i <= n; i++) f *= i;
            return f;
        }
        static void CreatePlot(double[] x, double[] yExp, double[] yTh, string yLabel,
            string title, string filename, ScottPlot.Alignment legendLocation = ScottPlot.Alignment.LowerRight)
        {
            ScottPlot.Plot myPlot = new();
            myPlot.Title(title);
            myPlot.XLabel("λ (интенсивность входного потока, заявок/с)");
            myPlot.YLabel(yLabel);
            var exp = myPlot.Add.Scatter(x, yExp);
            exp.LegendText = "Эксперимент";
            var th = myPlot.Add.Scatter(x, yTh);
            th.LegendText = "Теория";
            myPlot.Legend.Alignment = legendLocation;
            myPlot.SavePng(filename, 800, 500);
        }
    }
}