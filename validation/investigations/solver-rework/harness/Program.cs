using System.Text.Json;
using SpanDraft.Core.Beams; using SpanDraft.Core.Loads; using SpanDraft.Core.Materials; using SpanDraft.Core.Sections; using SpanDraft.Core.Supports; using SpanDraft.Core.Units; using SpanDraft.Solver;
static Length M(double m)=>Length.FromMeters(m);
using var w=new StreamWriter(args[1]);
foreach(var line in File.ReadLines(args[0])){
  using var d=JsonDocument.Parse(line); var c=d.RootElement;
  var o=new Dictionary<string,object?>{["id"]=c.GetProperty("id").GetString()};
  try{
    var mat=new Material("f",Pressure.FromPascals(c.GetProperty("E").GetDouble()),Pressure.FromPascals(235e6),MaterialStrengthType.Re);
    var sec=new CustomSection(Area.FromSquareMeters(c.GetProperty("A").GetDouble()),SecondMomentOfArea.FromMetersToTheFourth(c.GetProperty("I").GetDouble()),SectionModulus.FromCubicMeters(1));
    var sup=c.GetProperty("sup").EnumerateArray().Select(s=>new Support(M(s[0].GetDouble()),Enum.Parse<SupportType>(s[1].GetString()!)));
    var loads=c.GetProperty("pf").EnumerateArray().Select(p=>(BeamLoad)new PointForce(M(p[0].GetDouble()),Force.FromNewtons(p[1].GetDouble())))
      .Concat(c.GetProperty("pm").EnumerateArray().Select(p=>(BeamLoad)new PointMoment(M(p[0].GetDouble()),Moment.FromNewtonMeters(p[1].GetDouble()))))
      .Concat(c.GetProperty("q").EnumerateArray().Select(p=>(BeamLoad)new UniformDistributedLoad(M(p[0].GetDouble()),M(p[1].GetDouble()),ForcePerLength.FromNewtonsPerMeter(p[2].GetDouble()))));
    var hinges=c.TryGetProperty("hinges",out var hp)?hp.EnumerateArray().Select(h=>h.GetDouble()).ToArray():Array.Empty<double>();
    static double K(JsonElement e)=>e.ValueKind==JsonValueKind.String?double.PositiveInfinity:e.GetDouble();
    var springs=c.TryGetProperty("springs",out var sp)?sp.EnumerateArray().Select(s=>new ElasticSupport(s[0].GetDouble(),K(s[1]),K(s[2]),s[3].GetBoolean())).ToArray():Array.Empty<ElasticSupport>();
    var sol=new EulerBernoulliBeamSolver().Solve(new BeamModel(M(c.GetProperty("L").GetDouble()),mat,sec,sup,loads),new SolverLoads([],[],[],[],[],hinges,springs,
      c.TryGetProperty("ei",out var ep)?ep.EnumerateArray().Select(e=>new PointAction(e[0].GetDouble(),e[1].GetDouble())).ToArray():null,
      c.TryGetProperty("lin",out var lp)?lp.EnumerateArray().Select(e=>new LinearAction(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble(),e[3].GetDouble())).ToArray():null,
      c.TryGetProperty("curv",out var kp)?kp.EnumerateArray().Select(e=>new UniformAction(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble())).ToArray():null));
    o["R"]=sol.Nodes.Where(n=>n.ReactionY.HasValue).Select(n=>new double?[]{n.Position.Meters,n.ReactionY!.Value.Newtons,n.ReactionMoment?.NewtonMeters}).ToArray();
    o["S"]=c.GetProperty("pts").EnumerateArray().Select(p=>{var r=sol.EvaluateAt(M(p[0].GetDouble()),p[1].GetString()=="L"?EvaluationSide.Left:EvaluationSide.Right);return new[]{r.TransverseDisplacement.Meters,r.RotationRadians,r.ShearForce.Newtons,r.BendingMoment.NewtonMeters};}).ToArray();
    var e=sol.Extrema;
    object X<T>(BeamExtremum<T> b,double v)=>new object?[]{v,b.Position.Meters,b.Side?.ToString()};
    o["X"]=new Dictionary<string,object>{["wMin"]=X(e.MinimumTransverseDisplacement,e.MinimumTransverseDisplacement.Value.Meters),["wMax"]=X(e.MaximumTransverseDisplacement,e.MaximumTransverseDisplacement.Value.Meters),["VMin"]=X(e.MinimumShearForce,e.MinimumShearForce.Value.Newtons),["VMax"]=X(e.MaximumShearForce,e.MaximumShearForce.Value.Newtons),["MMin"]=X(e.MinimumBendingMoment,e.MinimumBendingMoment.Value.NewtonMeters),["MMax"]=X(e.MaximumBendingMoment,e.MaximumBendingMoment.Value.NewtonMeters)};
  }catch(BeamSolverException ex){o["fail"]=ex.Code.ToString();o["msg"]=ex.Message;}catch(Exception ex){o["crash"]=ex.GetType().Name+": "+ex.Message;}
  w.WriteLine(JsonSerializer.Serialize(o));
}
