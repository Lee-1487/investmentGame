using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using BlackWhale.Rules;
using BlackWhale.UI.Dev;

// Offline design model. Reuses the real pattern generator, NOT the production room or DevBot.
// No private future is available to decisions except explicitly sampled information.
// Units: one lot = five shares; max six lots/player, two lots/company/player.
internal static class DesignStudy
{
    static readonly string[] Ids = { "FOOD", "AUTO", "AI", "BIO" };
    public sealed class Variant {
        public string name;
        public int cadence = 5;
        public bool shared, bluff, reactive, authored, unlimited, preplan;
        public double hiddenScale = 1, reactionStrength = 1;
    }
    public sealed class Run {
        public int seed, winner;
        public double[][] assets = new double[6][];
        public double[] finalWithoutHidden = new double[4];
        public long blocked, requested, overlap, samples, decisions, changes, attacks, attackOpportunities;
        public double leaderDamage, forecastError;
        public long forecasts;
    }
    static int Leader(double[] a) => Enumerable.Range(0,4).OrderByDescending(i=>a[i]).ThenBy(i=>i).First();
    static double Clamp(double x, double a, double b) => Math.Max(a,Math.Min(b,x));
    static double Reaction(int c, double b, int n, bool on, bool hidden = false) {
        if (!on || n == 0) return b;
        if (c == 0) return b * (1 + .2 * (n-1));
        if (c == 2 && n >= 3) return b - 240; // -12 percentage points / five-day segment
        if (c == 3 && b > 1500) return n == 1 ? b : n == 2 ? b/3 : -b/3;
        return b;
    }
    static double Predict(int p,int c,int day,int end,double[,,] info,double[,,] history) {
        double sum=0;
        for(int d=day;d<=end;d++) {
            double v=info[p,c,d];
            if(double.IsNaN(v)) {
                var known=Enumerable.Range(1,25).Where(k=>!double.IsNaN(info[p,c,k]))
                    .OrderBy(k=>Math.Abs(k-d)).Take(3).ToArray();
                v=known.Length>0 ? known.Average(k=>info[p,c,k]) : 0;
                if(d>=26) v = day>1 ? history[p,c,day-1] * .35 : 0;
            }
            sum+=v;
        }
        return sum/(end-day+1);
    }
    static Run Simulate(int seed, Variant v, bool skillProbe=false) {
        var r=new Run{seed=seed};
        var lib=StockPatternLibrary.CreateDefault();
        double[] wealth={100,100,100,100}, noHidden={100,100,100,100};
        int[,] prior=new int[4,4];
        for(int turn=1;turn<=6;turn++) {
            var baseDaily=new double[4,31];
            var info=new double[4,4,31]; var history=new double[4,4,31];
            var rng=new Random(unchecked(seed*7919+turn*397));
            for(int c=0;c<4;c++) {
                var chart=PatternMarketGenerator.Inspect(seed,turn,Ids[c],library:lib).DailyChangesBps;
                for(int d=1;d<=30;d++) baseDaily[c,d]=chart[d]-chart[d-1];
            }
            if(v.authored) {
                // Every player assigns exactly two UP, one DOWN, one FLAT directions.
                for(int p=0;p<4;p++) {
                    var directions=new[]{1,1,-1,0}.OrderBy(_=>rng.Next()).ToArray();
                    for(int c=0;c<4;c++) {
                        int segment=(p+c)%4+1, start=segment*5+1;
                        double magnitude=Math.Max(200,Math.Abs(Enumerable.Range(start,5).Sum(d=>baseDaily[c,d]))/5);
                        for(int d=start;d<start+5;d++) baseDaily[c,d]=directions[c]*magnitude;
                    }
                }
            }
            for(int p=0;p<4;p++) for(int c=0;c<4;c++) {
                for(int d=0;d<=30;d++) info[p,c,d]=double.NaN;
                var privateDays=Enumerable.Range(6,20).OrderBy(_=>new Random(unchecked(seed*100003+turn*1009+p*107+c*31+_)).Next()).Take(skillProbe && p==0 ? 16 : 8).ToArray();
                for(int d=1;d<=5;d++) info[p,c,d]=baseDaily[c,d];
                foreach(int d in privateDays) info[p,c,d]=baseDaily[c,d];
                if(v.authored) {
                    int start=((p+c)%4+1)*5+1;
                    for(int d=start;d<start+5;d++) info[p,c,d]=baseDaily[c,d];
                }
            }
            if(v.shared) {
                var original=(double[,,])info.Clone();
                for(int p=0;p<4;p++) for(int c=0;c<4;c++) {
                    int other=(p+1)%4;
                    foreach(int d in Enumerable.Range(6,20).Where(d=>!double.IsNaN(original[other,c,d])).Take(4)) {
                        bool lie=v.bluff && (seed+turn+other+c)%4==0;
                        if (double.IsNaN(original[p,c,d])) info[p,c,d]=lie ? -original[other,c,d] : original[other,c,d];
                    }
                }
            }
            int[,] held=new int[4,4]; double[] gains=new double[4], hiddenGains=new double[4];
            int[,] lastOwners=new int[4,4];
            var frozen=new double[4,4,31];
            if(v.preplan) for(int p=0;p<4;p++) for(int c=0;c<4;c++) for(int d=6;d<=30;d++)
                frozen[p,c,d]=Predict(p,c,d,Math.Min(30,d+4),info,history);
            for(int day=6;day<=30;day++) {
                if((day-6)%v.cadence==0) {
                    int end=Math.Min(30,day+v.cadence-1);
                    var desired=new int[4,4]; int leader=Leader(wealth);
                    // Policies: adventurous, stable, profit-only, leader-aware.
                    for(int p=0;p<4;p++) {
                        r.decisions++;
                        var scores=new double[4];
                        for(int c=0;c<4;c++) {
                            double prediction=v.preplan ? frozen[p,c,day] : Predict(p,c,day,end,info,history);
                            double noise=((new Random(unchecked(seed*7919+turn*397+p*97+c*17+day)).NextDouble()*2)-1)*100;
                            int n=Enumerable.Range(0,4).Count(q=>lastOwners[q,c]>0 && q!=p)+1;
                            double expected=prediction + v.reactionStrength*(Reaction(c,prediction,n,v.reactive,day>=26)-prediction);
                            double preference=p==0 ? (c>=2?150:-50) : p==1 ? (c<2?180:-180):0;
                            double blocking=0;
                            if(p==3 && p!=leader && v.reactive && lastOwners[leader,c]>0) {
                                double before=Reaction(c,prediction,Math.Max(1,n-1),true);
                                blocking=Math.Max(0,before-expected)*lastOwners[leader,c]*.25;
                            }
                            scores[c]=expected+preference+noise+blocking;
                            r.forecastError+=Math.Abs(prediction-Enumerable.Range(day,end-day+1).Average(d=>baseDaily[c,d])); r.forecasts++;
                        }
                        int budget=6;
                        foreach(int c in Enumerable.Range(0,4).OrderByDescending(c=>scores[c])) {
                            if(scores[c]<=0 || budget==0) continue;
                            desired[p,c]=Math.Min(2,budget); budget-=desired[p,c];
                        }
                    }
                    held=new int[4,4];
                    // Fair rotating priority, shared across variants; one lot each pass.
                    for(int pass=0;pass<2;pass++) for(int seat=0;seat<4;seat++) {
                        int p=(seat+seed+turn+day)%4;
                        for(int c=0;c<4;c++) if(desired[p,c]>pass) {
                            r.requested++;
                            if(!v.unlimited && Enumerable.Range(0,4).Sum(q=>held[q,c])>=4) r.blocked++;
                            else held[p,c]++;
                        }
                    }
                    for(int p=0;p<4;p++) for(int c=0;c<4;c++) if(held[p,c]!=prior[p,c]) r.changes++;
                    prior=(int[,])held.Clone();
                }
                int lead=Leader(wealth);
                for(int c=0;c<4;c++) {
                    int n=Enumerable.Range(0,4).Count(p=>held[p,c]>0);
                    r.samples++; if(n>=2) r.overlap++;
                    double b=baseDaily[c,day] * (day>=26 ? v.hiddenScale : 1);
                    double actual=b + v.reactionStrength*(Reaction(c,b,n,v.reactive,day>=26)-b);
                    for(int p=0;p<4;p++) {
                        double gain=held[p,c]/6.0*actual/10000;
                        gains[p]+=gain; if(day>=26) hiddenGains[p]+=gain;
                        history[p,c,day]=actual;
                        // Everyone sees elapsed base samples; conditional adjustments are public.
                        info[p,c,day]=b;
                    }
                    if(v.reactive && held[lead,c]>0) for(int p=0;p<4;p++) if(p!=lead && held[p,c]>0) {
                        r.attackOpportunities++;
                        double without=b + v.reactionStrength*(Reaction(c,b,n-1,true,day>=26)-b);
                        double damage=(without-actual)*held[lead,c]/6.0;
                        if(damage>0) { r.attacks++; r.leaderDamage+=damage; }
                    }
                }
                lastOwners=(int[,])held.Clone();
            }
            for(int p=0;p<4;p++) {
                noHidden[p]*=Math.Max(0,1+gains[p]-hiddenGains[p]);
                wealth[p]*=Math.Max(0,1+gains[p]);
                if(double.IsNaN(wealth[p]) || wealth[p]<0) throw new Exception("Invalid wealth");
            }
            r.assets[turn-1]=(double[])wealth.Clone();
        }
        r.finalWithoutHidden=noHidden; r.winner=Leader(wealth);
        return r;
    }
    static object Summarize(List<Run> runs) {
        int n=runs.Count;
        double Rate(Func<Run,bool> f)=>100.0*runs.Count(f)/n;
        var wins=Enumerable.Range(0,4).Select(p=>runs.Count(r=>r.winner==p)).ToArray();
        int lateLast=runs.Count(r=>Enumerable.Range(0,4).All(p=>r.assets[2][r.winner]<=r.assets[2][p]) && r.assets[2].Distinct().Count()==4);
        int changed=runs.Count(r=>Leader(r.assets[2])!=r.winner);
        double ci=1.96*Math.Sqrt((double)changed/n*(1.0-changed/(double)n)/n)*100;
        return new {
            games=n,wins, midLeaderLostPct=Rate(r=>Leader(r.assets[2])!=r.winner), midLeaderLostCi95HalfWidth=ci,
            lastAtMidWonPct=100.0*lateLast/n,
            hiddenWinnerFlipPct=Rate(r=>Leader(r.finalWithoutHidden)!=r.winner),
            meanLeaderChanges=runs.Average(r=>Enumerable.Range(1,5).Count(t=>Leader(r.assets[t])!=Leader(r.assets[t-1]))),
            blockedDemandPct=100.0*runs.Sum(r=>r.blocked)/Math.Max(1,runs.Sum(r=>r.requested)),
            coholdingPct=100.0*runs.Sum(r=>r.overlap)/runs.Sum(r=>r.samples),
            damagingCoPresencePct=100.0*runs.Sum(r=>r.attacks)/Math.Max(1,runs.Sum(r=>r.attackOpportunities)),
            meanLeaderDamageBps=runs.Average(r=>r.leaderDamage),
            decisionsPerPlayerPerGame=runs.Average(r=>r.decisions)/4,
            changesPerPlayerPerGame=runs.Average(r=>r.changes)/4,
            forecastMaeBps=runs.Sum(r=>r.forecastError)/runs.Sum(r=>r.forecasts),
            meanFinalAssets=runs.Average(r=>r.assets[5].Average()),
            bankruptPlayerPct=100.0*runs.Sum(r=>r.assets[5].Count(a=>a==0))/(4*n)
        };
    }
    public static void Execute(string root,int repetitions, bool modelsOnly = false) {
        string dir=Path.Combine(root,"Logs","DesignStudy"); Directory.CreateDirectory(dir);
        var json=new JsonSerializerOptions{IncludeFields=true,WriteIndented=true};
        var settings=new BotExperimentSettings{repetitions=repetitions,firstSeed=1001,blockTrading=false};
        if (!modelsOnly) using(var baseline=new BotExperimentBatch(settings,StockPatternLibrary.CreateDefault())) {
            while(baseline.Running) baseline.Step();
            File.WriteAllText(Path.Combine(dir,"production-raw.json"),JsonSerializer.Serialize(baseline.Results,json));
            var runs=baseline.Results.runs;
            var result=new {
                games=runs.Count,wins=baseline.Summary.finalWins,
                midLeaderLostPct=100.0*runs.Count(r=>!r.turns[2].assetLeaders.Contains(r.finalWinner))/runs.Count,
                lastAtMidWonPct=100.0*runs.Count(r=>r.turns[2].assets.Distinct().Count()==4 && r.turns[2].assets[r.finalWinner]==r.turns[2].assets.Min())/runs.Count,
                coholdingPct=100.0*runs.Sum(r=>r.contestedCompanySamples)/runs.Sum(r=>r.companySamples),
                positionChangesPerPlayer=runs.Average(r=>r.positionChanges)/4,
                failures=runs.Sum(r=>r.commandFailures),
                failureReasons=runs.SelectMany(r=>r.failureReasons).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Value)),
                meanFinalAssets=runs.Average(r=>r.turns[5].assets.Average(a=>(double)a))
            };
            File.WriteAllText(Path.Combine(dir,"production-summary.json"),JsonSerializer.Serialize(result,json));
            Console.WriteLine("Production "+JsonSerializer.Serialize(result));
        }
        var variants=new[]{
            new Variant{name="M0_static_daily_random",cadence=1},
            new Variant{name="M1_static_5day_random"},
            new Variant{name="M2_static_5day_shared",shared=true},
            new Variant{name="M3_reactive_5day_shared",shared=true,reactive=true},
            new Variant{name="M4_reactive_5day_bluff",shared=true,reactive=true,bluff=true},
            new Variant{name="M5_authored_reactive_shared",shared=true,reactive=true,authored=true},
            new Variant{name="M6_reactive_unlimited_supply",shared=true,reactive=true,unlimited=true},
            new Variant{name="M7_static_preplanned30",preplan=true},
            new Variant{name="M8_mild_reactive_half_hidden",shared=true,reactive=true,hiddenScale=.5,reactionStrength=.5},
        };
        var summaries=new Dictionary<string,object>();
        foreach(var v in variants) {
            var runs=Enumerable.Range(1001,repetitions).Select(seed=>Simulate(seed,v)).ToList();
            var probe=Enumerable.Range(1001,repetitions).Select(seed=>Simulate(seed,v,true)).ToList();
            int delta=probe.Count(r=>r.winner==0)-runs.Count(r=>r.winner==0);
            var result=new{rules=v,metrics=Summarize(runs),moreInformationBot0WinDeltaPp=100.0*delta/repetitions};
            summaries[v.name]=result;
            File.WriteAllText(Path.Combine(dir,v.name+"-raw.json"),JsonSerializer.Serialize(runs,json));
            File.WriteAllText(Path.Combine(dir,v.name+"-information-probe.json"),JsonSerializer.Serialize(probe,json));
            Console.WriteLine(v.name+" "+JsonSerializer.Serialize(result));
            var again=Simulate(1001,v);
            if(JsonSerializer.Serialize(again,json)!=JsonSerializer.Serialize(runs[0],json)) throw new Exception("Replay mismatch");
        }
        File.WriteAllText(Path.Combine(dir,"model-summary.json"),JsonSerializer.Serialize(summaries,json));
    }

    public static void ValidateSavedLibrary(string root) {
        var saved=JsonSerializer.Deserialize<StockPatternLibrary>(File.ReadAllText(Path.Combine(root,"Assets/Resources/StockPatternLibrary.json")),
            new JsonSerializerOptions{IncludeFields=true});
        saved.UpgradeLegacy(); saved=saved.Copy();
        for(int seed=1001;seed<=1400;seed++) for(int turn=1;turn<=6;turn++) foreach(string id in Ids) {
            var a=PatternMarketGenerator.Inspect(seed,turn,id).DailyChangesBps;
            var b=PatternMarketGenerator.Inspect(seed,turn,id,library:saved).DailyChangesBps;
            if(!a.SequenceEqual(b)) throw new Exception("Saved pattern library differs: "+seed+"/"+turn+"/"+id);
        }
        Console.WriteLine("PASS saved-library equivalence: 400 seeds x 6 turns x 4 active companies");
    }
}
