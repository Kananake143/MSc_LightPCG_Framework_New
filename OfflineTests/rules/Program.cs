using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using LightPCG.Core;
class P {
  static void Main(){
    // 1) rule sanity: deflection is a bijection on directions and RotationFor round-trips
    var dirs=new[]{Vector2Int.right,Vector2Int.left,Vector2Int.up,Vector2Int.down};
    foreach(var t in new[]{TileType.Mirror,TileType.Refractor}) foreach(int r in OpticsRules.AllowedRotations(t)){
      var outs=dirs.Select(d=>OpticsRules.Deflect(t,r,d)).Distinct().Count(); if(outs!=4) throw new Exception("not bijective");
      foreach(var d in dirs){var o=OpticsRules.Deflect(t,r,d); int rr=OpticsRules.RotationFor(t,d,o);
        if(OpticsRules.EffectiveClass(t,rr)!=OpticsRules.EffectiveClass(t,r)) throw new Exception("roundtrip");}}
    // 1b) explicit Table 3.2 (Chapter 3): every type x orientation x incoming direction
    Vector2Int E=Vector2Int.right,W=Vector2Int.left,N=Vector2Int.up,S=Vector2Int.down;
    var table=new (TileType t,int r,Vector2Int[] ins,Vector2Int[] outs)[]{
      (TileType.Mirror,45,new[]{E,N,W,S},new[]{N,E,S,W}), (TileType.Mirror,225,new[]{E,N,W,S},new[]{N,E,S,W}),
      (TileType.Mirror,135,new[]{E,N,W,S},new[]{S,W,N,E}), (TileType.Mirror,315,new[]{E,N,W,S},new[]{S,W,N,E}),
      (TileType.Refractor,0,new[]{E,N,W,S},new[]{E,N,W,S}), (TileType.Refractor,180,new[]{E,N,W,S},new[]{E,N,W,S}),
      (TileType.Refractor,90,new[]{E,N,W,S},new[]{N,W,S,E}), (TileType.Refractor,270,new[]{E,N,W,S},new[]{S,E,N,W})};
    int cells=0; foreach(var row in table) for(int k=0;k<4;k++){ cells++;
      if(OpticsRules.Deflect(row.t,row.r,row.ins[k])!=row.outs[k]) throw new Exception($"Table 3.2 mismatch {row.t} {row.r} {row.ins[k]}"); }
    // agreement of the adopted refractor rule with the earlier solver (0/180 pass, 90/270 left)
    int agree=0; foreach(int r in new[]{0,90,180,270}) foreach(var d in new[]{E,N,W,S}){
      var old=(r==0||r==180)?d:new Vector2Int(-d.y,d.x); if(OpticsRules.Deflect(TileType.Refractor,r,d)==old) agree++; }
    Console.WriteLine($"rules ok (Table 3.2: {cells} combinations; refractor agreement with earlier solver {agree}/16)");
    // 2) random generator: axis-aligned path with bends
    int mism=0, ruleViol=0, scrFail=0; int built=0, rejected=0, certOk=0, reach=0, n=0; var mi=new List<float>(); var md=new List<float>(); var mr=new List<float>(); var mo=new List<float>();
    var sw=System.Diagnostics.Stopwatch.StartNew();
    for(int seed=0; seed<400; seed++){
      var rng=new System.Random(seed); var g=new GridModel(16,16);
      for(int x=0;x<16;x++){g.SetTile(x,0,TileType.Wall);g.SetTile(x,15,TileType.Wall);} for(int y=0;y<16;y++){g.SetTile(0,y,TileType.Wall);g.SetTile(15,y,TileType.Wall);}
      var em=new Vector2Int(0,rng.Next(2,14)); g.SetTile(em.x,em.y,TileType.Emitter); var ed=Vector2Int.right;
      int bendsN=rng.Next(2,9); var bends=new List<(Vector2Int,TileType)>(); var pos=em; var dir=ed; bool fail=false;
      var used=new HashSet<Vector2Int>{em};
      for(int i=0;i<bendsN;i++){ int len=rng.Next(2,6); var c=pos; for(int k=0;k<len;k++){c+=dir;} if(c.x<1||c.y<1||c.x>14||c.y>14||used.Contains(c)){fail=true;break;}
        for(var q=pos+dir;q!=c;q+=dir) used.Add(q); used.Add(c);
        var type=rng.Next(2)==0?TileType.Mirror:TileType.Refractor; bends.Add((c,type)); pos=c; dir=rng.Next(2)==0?new Vector2Int(-dir.y,dir.x):new Vector2Int(dir.y,-dir.x);}
      if(fail){continue;}
      // receiver straight ahead
      var rc=pos; do{rc+=dir;}while(rc.x>0&&rc.y>0&&rc.x<15&&rc.y<15&&!used.Contains(rc)); if(used.Contains(rc)) continue;
      g.SetTile(rc.x,rc.y,TileType.Receiver);
      foreach(var (c,t) in bends) g.SetTile(c.x,c.y,t);
      var decoys=new List<(Vector2Int,TileType)>(); int nd=rng.Next(0,5);
      while(decoys.Count<nd){var c=new Vector2Int(rng.Next(1,15),rng.Next(1,15)); if(g.GetTile(c.x,c.y)==TileType.Empty&&!used.Contains(c)){g.SetTile(c.x,c.y,TileType.Mirror);decoys.Add((c,TileType.Mirror));used.Add(c);}}
      for(int w=0;w<25;w++){var c=new Vector2Int(rng.Next(1,15),rng.Next(1,15)); if(g.GetTile(c.x,c.y)==TileType.Empty&&!used.Contains(c)) g.SetTile(c.x,c.y,TileType.Wall);}
      n++;
      var sol=IntendedSolution.Build(g,em,ed,rc,bends,decoys,rng);
      if(sol.FailureReason!=null){rejected++; continue;} built++;
      var scr=PuzzleScrambler.Scramble(g,em,ed,rc,sol,rng.Next(0,4),rng);
      if(scr.Initial==null){rejected++; scrFail++; continue;}
      if(scr.CertificateValid) certOk++; if(scr.CertificateReachable) reach++;
      var f=MobilityAnalyzer.Compute(g,em,ed,scr.Initial,sol);
      // carry-one-cell rule: every certificate action is a rotation in place or a carry of exactly one cell,
      // the certificate length is kRel + misoriented in-place pieces, and Apply rejects a two-cell carry
      foreach(var m in scr.Certificate){ int dd=Math.Abs(m.From.x-m.To.x)+Math.Abs(m.From.y-m.To.y); if(dd>1){ Console.WriteLine($"RULE VIOLATION seed {seed}: {m}"); ruleViol++; } }
      if(scr.Certificate.Count!=scr.KRelApplied+(int)Math.Round(f.Mo*sol.SolutionCount)){ Console.WriteLine($"CERT LENGTH seed {seed}: {scr.Certificate.Count} vs kRel {scr.KRelApplied} + Mo*N {f.Mo*sol.SolutionCount}"); ruleViol++; }
      if((int)Math.Round(f.Mr*sol.SolutionCount)!=scr.KRelApplied){ Console.WriteLine($"MR seed {seed}"); ruleViol++; }
      foreach(var kv in scr.Initial.Pieces){ var far=new Vector2Int(kv.Key.x+2,kv.Key.y); if(far.x<15 && !scr.Initial.Pieces.ContainsKey(far) && g.GetTile(far.x,far.y)==TileType.Empty){ var s3=scr.Initial.Clone(); if(s3.Apply(new Move{PieceId=kv.Value.Id,From=kv.Key,To=far,Rot=kv.Value.Rot},g)){ Console.WriteLine($"APPLY ACCEPTED 2-CELL CARRY seed {seed}"); ruleViol++; } break; } }
      { // naive check: evaluate every action without the shortcut
        var b0=BeamTracer.Trace(g,em,ed,scr.Initial.Pieces); int nA=0,nI=0,nD=0;
        var dec=new HashSet<int>(scr.Initial.Pieces.Values.Where(p=>p.IsDecoy).Select(p=>p.Id));
        var empty=new List<Vector2Int>(); for(int x=1;x<15;x++)for(int y=1;y<15;y++){var c=new Vector2Int(x,y); var t=g.GetTile(x,y); if(!scr.Initial.Pieces.ContainsKey(c)&&(t==TileType.Empty||OpticsRules.IsMovable(t))) empty.Add(c);}
        foreach(var kv in scr.Initial.Pieces.ToList()){ var p=kv.Value; int cc=OpticsRules.EffectiveClass(p.Type,p.Rot);
          var acts=new List<Move>(); foreach(int r in OpticsRules.ClassRepresentatives(p.Type)) if(OpticsRules.EffectiveClass(p.Type,r)!=cc) acts.Add(new Move{PieceId=p.Id,From=kv.Key,To=kv.Key,Rot=r});
          foreach(var to in empty) if(Math.Abs(to.x-kv.Key.x)+Math.Abs(to.y-kv.Key.y)==1) foreach(int r in OpticsRules.ClassRepresentatives(p.Type)) acts.Add(new Move{PieceId=p.Id,From=kv.Key,To=to,Rot=r});
          foreach(var m in acts){ nA++; var s2=scr.Initial.Clone(); s2.Apply(m,g); var b=BeamTracer.Trace(g,em,ed,s2.Pieces);
            if(b.HitReceiver!=b0.HitReceiver||!b.HitPieceIds.SequenceEqual(b0.HitPieceIds)){nI++; if(p.IsDecoy||b.HitPieceIds.Any(dec.Contains)) nD++;}}}
        if(nA!=f.NActions||nI!=f.NInteract||nD!=f.NDecoyInteract) { Console.WriteLine($"MISMATCH seed {seed}: {nA}/{f.NActions} {nI}/{f.NInteract} {nD}/{f.NDecoyInteract}"); mism++; }
      } mi.Add(f.Mi); md.Add(f.Md); mr.Add(f.Mr); mo.Add(f.Mo);
      var v=MidCalculator.Compute(sol.Solved.Pieces.Count,12,16,16,sol.SolutionCount,0,0,f);
      if(seed==1) LayoutExporter.Write("/tmp/cstest/out",seed,seed,g,em,ed,rc,sol,scr);
    }
    Func<List<float>,string> st=l=>$"min={l.Min():F3} mean={l.Average():F3} max={l.Max():F3} sd={Math.Sqrt(l.Select(x=>(x-l.Average())*(x-l.Average())).Average()):F3}";
    Console.WriteLine($"levels={n} built={built} rejected={rejected} certValid={certOk} reachable={reach} time={sw.ElapsedMilliseconds}ms");
    Console.WriteLine("naive-vs-fast mismatches="+mism+" scrambleFailures="+scrFail+" ruleViolations="+ruleViol+" rule="+MovementRule.Describe()); Console.WriteLine("Mr "+st(mr)); Console.WriteLine("Mo "+st(mo)); Console.WriteLine("Mi "+st(mi)); Console.WriteLine("Md "+st(md));
  }
}
