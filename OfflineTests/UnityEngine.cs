using System;
namespace UnityEngine {
  public struct Vector2Int : IEquatable<Vector2Int> {
    public int x, y; public Vector2Int(int x,int y){this.x=x;this.y=y;}
    public static Vector2Int zero=>new Vector2Int(0,0); public static Vector2Int one=>new Vector2Int(1,1);
    public static Vector2Int right=>new Vector2Int(1,0); public static Vector2Int left=>new Vector2Int(-1,0);
    public static Vector2Int up=>new Vector2Int(0,1); public static Vector2Int down=>new Vector2Int(0,-1);
    public static Vector2Int operator+(Vector2Int a,Vector2Int b)=>new Vector2Int(a.x+b.x,a.y+b.y);
    public static Vector2Int operator-(Vector2Int a,Vector2Int b)=>new Vector2Int(a.x-b.x,a.y-b.y);
    public static Vector2Int operator*(Vector2Int a,int k)=>new Vector2Int(a.x*k,a.y*k);
    public static Vector2Int operator-(Vector2Int a)=>new Vector2Int(-a.x,-a.y);
    public static bool operator==(Vector2Int a,Vector2Int b)=>a.x==b.x&&a.y==b.y;
    public static bool operator!=(Vector2Int a,Vector2Int b)=>!(a==b);
    public bool Equals(Vector2Int o)=>this==o; public override bool Equals(object o)=>o is Vector2Int v&&this==v;
    public override int GetHashCode()=>x*73856093^y*19349663; public override string ToString()=>$"({x}, {y})";
  }
  public static class Mathf {
    public static float Abs(float v)=>Math.Abs(v); public static int Abs(int v)=>Math.Abs(v);
    public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(b,v));
    public static int Max(int a,int b)=>Math.Max(a,b); public static int Min(params int[] v){int m=v[0]; foreach(var x in v) m=Math.Min(m,x); return m;}
    public static float DeltaAngle(float c,float t){float d=(t-c)%360f; if(d>180f)d-=360f; if(d<-180f)d+=360f; return d;}
  }
  public static class Random { static System.Random r=new System.Random(0); public static void InitState(int s){r=new System.Random(s);} public static int Range(int a,int b)=>r.Next(a,b); public static float value=>(float)r.NextDouble(); }
  public static class Debug { public static void Log(object o){} public static void LogWarning(object o){} public static void LogError(object o){} }
}
