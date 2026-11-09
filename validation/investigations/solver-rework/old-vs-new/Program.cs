using SpanDraft.Core.Beams; using SpanDraft.Core.Loads; using SpanDraft.Core.Materials; using SpanDraft.Core.Sections; using SpanDraft.Core.Supports; using SpanDraft.Core.Units; using SpanDraft.Solver;
using System.Globalization;
CultureInfo.DefaultThreadCurrentCulture=CultureInfo.InvariantCulture; CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
static Length M(double m)=>Length.FromMeters(m);
var mat=new Material("S235",Pressure.FromPascals(210e9),Pressure.FromPascals(235e6),MaterialStrengthType.Re);
var sec=new CustomSection(Area.FromSquareMeters(1e-3),SecondMomentOfArea.FromMetersToTheFourth(8.33e-6),SectionModulus.FromCubicMeters(1e-4));
const double L=1.0;
static BeamLoad F(double x,double v)=>new PointForce(M(x),Force.FromNewtons(v));
static BeamLoad Q(double a,double b,double v)=>new UniformDistributedLoad(M(a),M(b),ForcePerLength.FromNewtonsPerMeter(v));
void Run(string label,(double x,SupportType t)[] sup,BeamLoad[] loads,double x1=double.NaN,double x2=double.NaN){
  try{
    var s=new EulerBernoulliBeamSolver().Solve(new BeamModel(M(L),mat,sec,sup.Select(p=>new Support(M(p.x),p.t)),loads));
    var e=s.Extrema;
    var w=Math.Max(Math.Abs(e.MinimumTransverseDisplacement.Value.Meters),Math.Abs(e.MaximumTransverseDisplacement.Value.Meters));
    var V=Math.Max(Math.Abs(e.MinimumShearForce.Value.Newtons),Math.Abs(e.MaximumShearForce.Value.Newtons));
    var Mx=Math.Max(Math.Abs(e.MinimumBendingMoment.Value.NewtonMeters),Math.Abs(e.MaximumBendingMoment.Value.NewtonMeters));
    var rs=s.Nodes.Where(n=>n.ReactionY.HasValue).Select(n=>$"{n.Position.Meters:G6}:{n.ReactionY!.Value.Newtons:G6}"+(n.ReactionMoment.HasValue?$"/M{n.ReactionMoment.Value.NewtonMeters:G6}":""));
    string extra="";
    if(!double.IsNaN(x1)){
      var pr=s.Nodes.Where(n=>n.ReactionY.HasValue&&n.Position.Meters>=x1&&n.Position.Meters<=x2).ToArray();
      double net=pr.Sum(n=>n.ReactionY!.Value.Newtons);
      double couple=pr.Sum(n=>n.ReactionY!.Value.Newtons*(n.Position.Meters-x1));
      // Vmax outside the pair: scan both sides of every node outside (x1,x2)
      double vout=0;
      foreach(var n in s.Nodes){ double xx=n.Position.Meters; if(xx<=x1&&xx>0) vout=Math.Max(vout,Math.Abs(s.EvaluateAt(n.Position,EvaluationSide.Left).ShearForce.Newtons)); if(xx<x1) vout=Math.Max(vout,Math.Abs(s.EvaluateAt(n.Position,EvaluationSide.Right).ShearForce.Newtons)); if(xx>=x2&&xx<L) vout=Math.Max(vout,Math.Abs(s.EvaluateAt(n.Position,EvaluationSide.Right).ShearForce.Newtons)); if(xx>x2) vout=Math.Max(vout,Math.Abs(s.EvaluateAt(n.Position,EvaluationSide.Left).ShearForce.Newtons)); }
      extra=$" | Paar: Summe {net:G8} N, Moment {couple:G8} Nm, Vaussen {vout:G7}";
    }
    Console.WriteLine($"{label,-26} w {w*1e3:G6} mm  Vmax {V:G6} N  Mmax {Mx:G6} Nm  R[{string.Join(" ",rs)}]{extra}");
  }catch(BeamSolverException ex){Console.WriteLine($"{label,-26} ABGELEHNT {ex.Code}");}
}
double[] gaps={1e-2,1e-3,1e-4,1e-5,1e-7,6e-9};
var P=SupportType.Pinned;var R=SupportType.Roller;var C=SupportType.Fixed;
Console.WriteLine("=== A Endfeld: F=1 kN in Feldmitte, Lager links + rechts");
Run("ein Lager",[(0,P),(L,R)],[F(.5,1000)]);
Run("Einspannung",[(0,C),(L,R)],[F(.5,1000)]);
foreach(var a in gaps) Run($"Paar a={a:G3} m",[(0,P),(a,R),(L,R)],[F(.5,1000)],0,a);
Console.WriteLine("=== B Mittellager, einseitige Last: F=1 kN bei 0,25");
Run("ein Lager",[(0,P),(.5,R),(L,R)],[F(.25,1000)]);
Run("Einspannung",[(0,P),(.5,C),(L,R)],[F(.25,1000)]);
foreach(var a in gaps) Run($"Paar a={a:G3} m",[(0,P),(.5,R),(.5+a,R),(L,R)],[F(.25,1000)],.5,.5+a);
Console.WriteLine("=== C Mittellager, symmetrische Last: q=2 kN/m ueberall");
Run("ein Lager",[(0,P),(.5,R),(L,R)],[Q(0,L,2000)]);
Run("Einspannung",[(0,P),(.5,C),(L,R)],[Q(0,L,2000)]);
foreach(var a in new[]{1e-3,1e-5,6e-9}) Run($"Paar a={a:G3} m",[(0,P),(.5-a/2,R),(.5+a/2,R),(L,R)],[Q(0,L,2000)],.5-a/2,.5+a/2);
Console.WriteLine("=== D Kragarm nur durch das Paar: F=1 kN am freien Ende");
Run("ein Lager",[(0,P)],[F(L,1000)]);
Run("Einspannung",[(0,C)],[F(L,1000)]);
foreach(var a in gaps) Run($"Paar a={a:G3} m",[(0,P),(a,R)],[F(L,1000)],0,a);
Console.WriteLine("=== E Einspannung + nahes Lager: F=1 kN in Feldmitte");
Run("nur Einspannung+Rolle",[(0,C),(L,R)],[F(.5,1000)]);
foreach(var a in new[]{1e-3,1e-5,6e-9}) Run($"Eins.+Lager a={a:G3} m",[(0,C),(a,R),(L,R)],[F(.5,1000)],0,a);
Console.WriteLine("=== F Exakt gleiche Position, Festlager + Loslager: F=1 kN in Feldmitte");
try { Run("Paar a=0",[(0,P),(0,R),(L,R)],[F(.5,1000)]); } catch(Exception ex){ Console.WriteLine("Paar a=0 Ausnahme "+ex.GetType().Name+": "+ex.Message); }
Run("Paar a=1E-15",[(0,P),(1e-15,R),(L,R)],[F(.5,1000)],0,1e-15);
