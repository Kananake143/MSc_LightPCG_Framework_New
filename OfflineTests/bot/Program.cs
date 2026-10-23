using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using LightPCG.Core;
class P {
  // replicates GridVisualizer.GenerateLevel(seed) + BatchRunner design (no Unity scene)
  static Vector2Int Find(GridModel g, TileType t){ for(int x=0;x<g.Width;x++)for(int y=0;y<g.Height;y++) if(g.GetTile(x,y)==t) return new Vector2Int(x,y); return -Vector2Int.one; }
  static Vector2Int EmDir(GridModel g, Vector2Int e){ int dL=e.x,dR=g.Width-1-e.x,dB=e.y,dT=g.Height-1-e.y; int m=Mathf.Min(dL,dR,dB,dT);
    if(m==dL) return Vector2Int.right; if(m==dR) return Vector2Int.left; if(m==dB) return new Vector2Int(0,1); return new Vector2Int(0,-1); }
  // independent check of a bot plan: legal, walkable from the bot's position, ends with the receiver hit
  static bool ValidatePlan(GridModel g, Vector2Int em, Vector2Int ed, PuzzleState init, Vector2Int start, List<Move> plan){
    var s=init.Clone(); var agent=start;
    foreach(var m in plan){
      var dist=Navigation.Distances(g,s.Pieces,agent);
      var st=Navigation.StandingCell(dist,m.From,-Vector2Int.one); if(!st.HasValue) return false;
      var without=new Dictionary<Vector2Int,Piece>(s.Pieces); without.Remove(m.From);
      if(m.From!=m.To){ var d2=Navigation.Distances(g,without,st.Value); var st2=Navigation.StandingCell(d2,m.To,m.To); if(!st2.HasValue) return false; agent=st2.Value; } else agent=st.Value;
      if(!s.Apply(m,g)) return false;
    }
    return BeamTracer.Trace(g,em,ed,s.Pieces).HitReceiver;
  }
  static int RSTEPS=1;
  static void Main(string[] a){ if(a.Length>9) RSTEPS=int.Parse(a[9]);
    int MAXA=a.Length>3?int.Parse(a[3]):20; int runs=a.Length>0?int.Parse(a[0]):950, baseSeed=a.Length>1?int.Parse(a[1]):20262016, stride=a.Length>2?int.Parse(a[2]):1;
    var reasons=new Dictionary<string,int>(); var firstReasons=new Dictionary<string,int>();
    int valid=0, totalAttempts=0, decShort=0; var rows=new List<string>();
    var sw=System.Diagnostics.Stopwatch.StartNew(); var botRows=new List<string>(); var cfg=new BotConfig(); if(a.Length>4) cfg.MaxExpanded=int.Parse(a[4]); if(a.Length>5) cfg.HeuristicWeight=float.Parse(a[5]); if(a.Length>6) cfg.SourcesPerType=int.Parse(a[6]); if(a.Length>7){ int mc=int.Parse(a[7]); if(mc<0) MovementRule.AllowRelocation=false; else MovementRule.MaxCarryDistance=mc; } if(a.Length>8) cfg.TimeLimitMs=double.Parse(a[8]);
    for(int i=0;i<runs;i++){
      int run=i+1, seed=baseSeed+run; int j=(int)(((long)i*stride)%140);
      int steps=3+j%7, decoys=new[]{0,1,2,3,4}[(j/7)%5], kRel=(j/35)%4;
      UnityEngine.Random.InitState(seed); var rng=new System.Random(seed);
      bool ok=false; string why=null; int att=0; GridModel g=null; IntendedSolution sol=null; ScrambleResult scr=null; BackwardChainingGenerator pcg=null; Vector2Int em=default, ed=default, rc=default;
      for(att=1;att<=MAXA;att++){
        g=new GridModel(16,16); pcg=new BackwardChainingGenerator(g); pcg.GenerateValidPuzzle(steps,1,decoys);
        em=Find(g,TileType.Emitter); rc=Find(g,TileType.Receiver);
        if(em==-Vector2Int.one||rc==-Vector2Int.one){why="missing emitter or receiver";}
        else { ed=EmDir(g,em);
          if(!pcg.MainChainComplete) why="chain incomplete: "+pcg.ChainFailureReason.Split(' ')[0]+" "+(pcg.ChainFailureReason.Contains("occupied")?"occupied":pcg.ChainFailureReason.Contains("length 0")?"len0":pcg.ChainFailureReason.Contains("fallback")?"emitter fallback":"emitter blocked");
          else if(pcg.EmitterCell!=em||pcg.EmitterBeamDir!=ed) why="emitter facing mismatch";
          else { sol=IntendedSolution.Build(g,em,ed,rc,pcg.OrderedSolutionBends,pcg.DecoyPlacements,rng);
            if(sol.FailureReason!=null) why=sol.FailureReason.Split(' ')[0]+" "+(sol.FailureReason.Contains("interference")?"interference":sol.FailureReason.Contains("receiver")?"no-receiver":sol.FailureReason.Contains("first")?"first-bend":sol.FailureReason);
            else { scr=PuzzleScrambler.Scramble(g,em,ed,rc,sol,kRel,rng,30,true,RSTEPS);
              if(scr.Initial==null) why="scramble failed"; else { ok=true; break; } } } }
        reasons[why]=reasons.GetValueOrDefault(why)+1;
      }
      totalAttempts+=Math.Min(att,MAXA);
      if(!ok){ rows.Add($"{run},{steps},{decoys},{kRel},0"); continue; }
      valid++; if(pcg.DecoyCount<decoys) decShort++;
      // write scrambled state to grid as GridVisualizer does
      for(int x=0;x<16;x++)for(int y=0;y<16;y++) if(OpticsRules.IsMovable(g.GetTile(x,y))) g.SetTile(x,y,TileType.Empty);
      foreach(var kv in scr.Initial.Pieces) g.SetTile(kv.Key.x,kv.Key.y,kv.Value.Type);
      var f=MobilityAnalyzer.Compute(g,em,ed,scr.Initial,sol);
      var mid=MidCalculator.Compute(pcg.TotalObjectCount,12,16,16,pcg.SolutionObjectCount,pcg.RuleTransitions,pcg.RefractorCount,f);
      // ── bot: observes the scene only (no solution, no decoy flags) ──
      var view=new PuzzleState(); int pid=0;
      for(int x=0;x<16;x++)for(int y=0;y<16;y++){ var c=new Vector2Int(x,y); if(scr.Initial.Pieces.TryGetValue(c,out var pc)) view.Pieces[c]=new Piece{Id=pid++,Type=pc.Type,Rot=pc.Rot}; }
      var start=Navigation.StartCell(g,view.Pieces,em);
      var pr=BotPlanner.Plan(g,em,ed,rc,view,start,cfg);
      bool pv=pr.Found && ValidatePlan(g,em,ed,view,start,pr.Plan);
      botRows.Add($"{run},{steps},{decoys},{kRel},{pr.Outcome},{(pr.Found?1:0)},{(pv?1:0)},{pr.Expanded},{pr.Generated},{pr.TimeMs:F1},{pr.Plan.Count},{scr.Certificate.Count},{f.Mr:F4},{f.Mo:F4},{f.Mi:F4},{f.Md:F4},{mid.MidM:F4},{mid.MidStatic:F4},{pcg.TotalObjectCount}");
      rows.Add($"{run},{steps},{decoys},{kRel},1,{att},{pcg.SolutionObjectCount},{pcg.DecoyCount},{pcg.TotalObjectCount},{pcg.RefractorCount},{pcg.RuleTransitions},{(scr.CertificateValid?1:0)},{(scr.CertificateReachable?1:0)},{scr.Certificate.Count},{mid.ClNorm:F4},{mid.Ch:F4},{mid.Ci:F4},{mid.MidStatic:F4},{f.Mr:F4},{f.Mo:F4},{f.Mi:F4},{f.Md:F4},{mid.MidMobility:F4},{mid.MidM:F4},{mid.MidLegacy:F4},{f.NActions},{f.NInteract}");
    }
    System.IO.File.WriteAllText("bot.csv","Run,Steps,Decoys,KRel,Outcome,Found,PlanValid,Expanded,Generated,TimeMs,PlanLen,CertLen,Mr,Mo,Mi,Md,MIDM,MIDstatic,TotalObjects\n"+string.Join("\n",botRows));
    System.IO.File.WriteAllText("sim.csv","Run,Steps,Decoys,KRel,LevelValid,Attempts,SolutionObjects,DecoysPlaced,TotalObjects,Refractors,RuleTransitions,CertValid,CertReach,CertLen,ClNorm,Ch,Ci,MIDstatic,Mr,Mo,Mi,Md,MIDmob,MIDM,MIDlegacy,NActions,NInteract\n"+string.Join("\n",rows));
    { int nF=botRows.Count(r=>r.Split(',')[5]=="1"), nV=botRows.Count(r=>r.Split(',')[6]=="1"); Console.WriteLine($"bot: instances={botRows.Count} planFound={nF} planValidByIndependentChecker={nV}"); }
    Console.WriteLine($"runs={runs} valid={valid} ({100.0*valid/runs:F1}%) meanAttempts={1.0*totalAttempts/runs:F2} decoyShortfall={decShort} time={sw.ElapsedMilliseconds}ms");
    foreach(var kv in reasons.OrderByDescending(k=>k.Value)) Console.WriteLine($"  attempt outcome: {kv.Key ?? "OK"} = {kv.Value}");
  }
}
