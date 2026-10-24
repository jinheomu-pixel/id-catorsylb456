// A small MQL5 runtime in C++ for the IDCator MT5 sources (tools/mql-check).
//
// check.py translates an indicator to C++ and compiles it against this file:
//  - syntax/type check: g++ -fsyntax-only (names, argument counts and types);
//  - simulation (--run): the indicator runs tick by tick on a tick file like on an MT5 chart: tick history
//    (CopyTicksRange), M1 bars, OnCalculate on every tick, OnTimer every second and a table of chart objects.
//    Array index out of range and zero divide stop the run, like MT5's critical runtime errors.
// Only what the IDCator sources use is implemented.
#pragma once
#include <algorithm>
#include <cfenv>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <deque>
#include <map>
#include <string>
#include <type_traits>
#include <vector>

typedef unsigned char uchar;
typedef unsigned short ushort;
typedef unsigned int uint;
// ulong (unsigned long, 64-bit on Linux) comes from <sys/types.h>
typedef long long datetime;
typedef unsigned int color;

[[noreturn]] inline void MqlCritical(const std::string &what)
  {
   std::fprintf(stderr, "MQL5 CRITICAL ERROR: %s\n", what.c_str());
   std::exit(3);
  }

// ---------------------------------------------------------------- string
struct MqlString
  {
   std::string s;
   MqlString() {}
   MqlString(const char *c) : s(c) {}
   explicit MqlString(const std::string &v) : s(v) {}
   MqlString operator+(const MqlString &o) const { return MqlString(s + o.s); }
   MqlString &operator+=(const MqlString &o) { s += o.s; return *this; }
   bool operator==(const MqlString &o) const { return s == o.s; }
   bool operator!=(const MqlString &o) const { return s != o.s; }
  };
typedef MqlString string;

// ---------------------------------------------------------------- dynamic arrays (bounds-checked like MQL5)
template <class T>
struct MqlArr
  {
   std::deque<T> v;   // not vector: vector<bool> has no bool& element
   T &operator[](long i)
     {
      if(i < 0 || (size_t)i >= v.size())
         MqlCritical("array out of range: index " + std::to_string(i) + ", size " + std::to_string(v.size()));
      return v[(size_t)i];
     }
   const T &operator[](long i) const
     {
      if(i < 0 || (size_t)i >= v.size())
         MqlCritical("array out of range: index " + std::to_string(i) + ", size " + std::to_string(v.size()));
      return v[(size_t)i];
     }
  };
const int WHOLE_ARRAY = -1;
template <class T> int ArraySize(const MqlArr<T> &a) { return (int)a.v.size(); }
template <class T, size_t N> int ArraySize(const T (&)[N]) { return (int)N; }
template <class T> int ArrayResize(MqlArr<T> &a, int n, int reserve = 0) { a.v.resize((size_t)std::max(0, n)); return n; }
template <class T> void ArrayFree(MqlArr<T> &a) { a.v.clear(); }
template <class T> bool ArrayRemove(MqlArr<T> &a, uint start, uint count = 0xFFFFFFFF)
  {
   if(start >= a.v.size())
      return false;
   size_t end = count == 0xFFFFFFFF ? a.v.size() : std::min(a.v.size(), (size_t)start + count);
   a.v.erase(a.v.begin() + start, a.v.begin() + end);
   return true;
  }
template <class T> int ArrayCopy(MqlArr<T> &dst, const MqlArr<T> &src, int dst_start = 0, int src_start = 0, int count = WHOLE_ARRAY)
  {
   int n = count < 0 ? (int)src.v.size() - src_start : std::min(count, (int)src.v.size() - src_start);
   if(n <= 0)
      return 0;
   if((int)dst.v.size() < dst_start + n)
      dst.v.resize((size_t)(dst_start + n));
   for(int i = 0; i < n; i++)
      dst.v[(size_t)(dst_start + i)] = src.v[(size_t)(src_start + i)];
   return n;
  }
template <class T> int ArrayMaximum(const MqlArr<T> &a, int start = 0, int count = WHOLE_ARRAY)
  {
   int end = count < 0 ? (int)a.v.size() : std::min((int)a.v.size(), start + count);
   int best = -1;
   for(int i = start; i < end; i++)
      if(best < 0 || a.v[(size_t)i] > a.v[(size_t)best])
         best = i;
   return best;
  }
template <class T, class V> int ArrayInitialize(MqlArr<T> &a, V value) { for(auto &x : a.v) x = (T)value; return (int)a.v.size(); }
template <class T> bool ArraySetAsSeries(MqlArr<T> &a, bool flag) { return !flag; }
template <class T> bool ArraySort(MqlArr<T> &a) { std::sort(a.v.begin(), a.v.end()); return true; }

// ---------------------------------------------------------------- enums and constants
enum ENUM_TIMEFRAMES { PERIOD_CURRENT = 0, PERIOD_M1 = 1, PERIOD_M2 = 2, PERIOD_M3 = 3, PERIOD_M4 = 4, PERIOD_M5 = 5, PERIOD_M6 = 6,
                       PERIOD_M10 = 10, PERIOD_M12 = 12, PERIOD_M15 = 15, PERIOD_M20 = 20, PERIOD_M30 = 30,
                       PERIOD_H1 = 16385, PERIOD_H2 = 16386, PERIOD_H3 = 16387, PERIOD_H4 = 16388, PERIOD_H6 = 16390,
                       PERIOD_H8 = 16392, PERIOD_H12 = 16396, PERIOD_D1 = 16408, PERIOD_W1 = 32769, PERIOD_MN1 = 49153 };
enum ENUM_OBJECT { OBJ_RECTANGLE, OBJ_TEXT, OBJ_TREND, OBJ_HLINE, OBJ_LABEL };
enum ENUM_OBJECT_PROPERTY_INTEGER { OBJPROP_COLOR, OBJPROP_STYLE, OBJPROP_WIDTH, OBJPROP_BACK, OBJPROP_FILL, OBJPROP_SELECTABLE,
                                    OBJPROP_HIDDEN, OBJPROP_TIME, OBJPROP_FONTSIZE, OBJPROP_ANCHOR, OBJPROP_RAY_RIGHT, OBJPROP_RAY_LEFT };
enum ENUM_OBJECT_PROPERTY_DOUBLE { OBJPROP_PRICE };
enum ENUM_OBJECT_PROPERTY_STRING { OBJPROP_TEXT, OBJPROP_FONT };
enum ENUM_ANCHOR_POINT { ANCHOR_LEFT_UPPER, ANCHOR_LEFT, ANCHOR_LEFT_LOWER, ANCHOR_LOWER, ANCHOR_RIGHT_LOWER, ANCHOR_RIGHT,
                         ANCHOR_RIGHT_UPPER, ANCHOR_UPPER, ANCHOR_CENTER };
enum ENUM_LINE_STYLE { STYLE_SOLID, STYLE_DASH, STYLE_DOT, STYLE_DASHDOT, STYLE_DASHDOTDOT };
enum ENUM_CHART_PROPERTY_INTEGER { CHART_COLOR_BACKGROUND };
enum ENUM_SYMBOL_INFO_DOUBLE { SYMBOL_POINT, SYMBOL_TRADE_TICK_SIZE };
enum ENUM_INIT_RETCODE { INIT_SUCCEEDED = 0, INIT_FAILED = 1 };
const uint COPY_TICKS_ALL = 0xFFFFFFFF, COPY_TICKS_INFO = 1, COPY_TICKS_TRADE = 2;
const uint TICK_FLAG_BID = 2, TICK_FLAG_ASK = 4, TICK_FLAG_LAST = 8, TICK_FLAG_VOLUME = 16, TICK_FLAG_BUY = 32, TICK_FLAG_SELL = 64;
const color clrWhite = 0xFFFFFF, clrGray = 0x808080, clrLimeGreen = 0x32CD32, clrRed = 0x0000FF, clrNONE = 0xFFFFFFFF;

struct MqlTick
  {
   datetime time;
   double bid;
   double ask;
   double last;
   ulong volume;
   long time_msc;
   uint flags;
   double volume_real;
  };

// MQL5 colour literal C'r,g,b'
inline constexpr color MqlRgb(int r, int g, int b) { return (color)((r & 255) | ((g & 255) << 8) | ((b & 255) << 16)); }

// ---------------------------------------------------------------- simulated terminal
namespace Sim
  {
   struct Bar { datetime t; double o, h, l, c; long tv; };
   struct Obj
     {
      int type = 0;
      long time[2] = {0, 0};
      double price[2] = {0, 0};
      std::map<int, long> ints;
      std::string text, font;
      long seq = 0;
     };
   inline std::vector<MqlTick> ticks;       // whole tape
   inline size_t avail = 0;                 // ticks that have arrived
   inline std::vector<Bar> bars;            // chart bars (M1) of the arrived ticks
   inline bool barsFromLast = true;         // exchange symbol: bars from Last, CFD: from Bid
   inline datetime wall = 0;                // server / local clock
   inline double point = 0.25, tickSize = 0.25;
   inline int digits = 2;
   inline color background = MqlRgb(19, 23, 34);
   inline std::map<std::string, Obj> objects;
   inline long objSeq = 0;
   inline std::vector<std::pair<datetime, std::string>> alerts;
   inline std::vector<std::string> prints;
   inline std::string comment;
   inline int timerSeconds = 0;

   inline int Seconds(int tf)
     {
      if(tf == PERIOD_CURRENT || tf == PERIOD_M1)
         return 60;
      if(tf == PERIOD_W1)
         return 604800;
      if(tf == PERIOD_MN1)
         return 2592000;
      if(tf & 0x4000)
         return (tf & 0xFF) * 3600;
      return tf * 60;
     }
   inline datetime Align(datetime t, int sec)
     {
      if(sec == 604800)
        {
         long days = t / 86400;
         long dow = (days + 4) % 7;   // 0 = Sunday (1970-01-01 was a Thursday)
         return (days - dow) * 86400;
        }
      return t / sec * sec;
     }
   inline double BarPrice(const MqlTick &k) { return barsFromLast ? k.last : k.bid; }
   inline bool MakesBar(const MqlTick &k) { return barsFromLast ? (k.flags & TICK_FLAG_LAST) != 0 && k.last > 0 : k.bid > 0; }
   //--- one more tick arrived: update the chart bars
   inline void Arrive()
     {
      const MqlTick &k = ticks[avail++];
      if(!MakesBar(k))
         return;
      double p = BarPrice(k);
      datetime bt = Align(k.time, 60);
      if(bars.empty() || bars.back().t != bt)
         bars.push_back({bt, p, p, p, p, 1});
      else
        {
         Bar &b = bars.back();
         b.h = std::max(b.h, p);
         b.l = std::min(b.l, p);
         b.c = p;
         b.tv++;
        }
     }
   //--- bars of any timeframe (aggregated from the chart bars), oldest first
   inline std::vector<Bar> BarsOf(int tf)
     {
      int sec = Seconds(tf);
      if(sec == 60)
         return bars;
      std::vector<Bar> out;
      for(const Bar &b : bars)
        {
         datetime t = Align(b.t, sec);
         if(out.empty() || out.back().t != t)
            out.push_back({t, b.o, b.h, b.l, b.c, b.tv});
         else
           {
            Bar &o = out.back();
            o.h = std::max(o.h, b.h);
            o.l = std::min(o.l, b.l);
            o.c = b.c;
            o.tv += b.tv;
           }
        }
      return out;
     }
  }

// ---------------------------------------------------------------- predefined variables
inline string _Symbol("NQ");
inline ENUM_TIMEFRAMES _Period = PERIOD_M1;
inline double _Point = 0.25;
inline int _Digits = 2;

// ---------------------------------------------------------------- market data
inline int CopyTicksRange(const string &symbol, MqlArr<MqlTick> &out, uint flags = COPY_TICKS_ALL, ulong from_msc = 0, ulong to_msc = 0)
  {
   out.v.clear();
   auto first = std::lower_bound(Sim::ticks.begin(), Sim::ticks.begin() + Sim::avail, (long)from_msc,
                                 [](const MqlTick &k, long msc) { return k.time_msc < msc; });
   for(auto it = first; it != Sim::ticks.begin() + Sim::avail; ++it)
     {
      if(to_msc != 0 && it->time_msc > (long)to_msc)
         break;
      bool trade = (it->flags & (TICK_FLAG_LAST | TICK_FLAG_BUY | TICK_FLAG_SELL)) != 0;
      bool info = (it->flags & (TICK_FLAG_BID | TICK_FLAG_ASK)) != 0;
      if(flags == COPY_TICKS_TRADE && !trade)
         continue;
      if(flags == COPY_TICKS_INFO && !info)
         continue;
      out.v.push_back(*it);
     }
   return (int)out.v.size();
  }
inline int Bars(const string &symbol, ENUM_TIMEFRAMES tf) { return (int)Sim::BarsOf(tf).size(); }
inline int CopyTime(const string &symbol, ENUM_TIMEFRAMES tf, int start, int count, MqlArr<datetime> &out)
  {
   std::vector<Sim::Bar> b = Sim::BarsOf(tf);
   int total = (int)b.size();
   int newest = total - 1 - start;
   int oldest = std::max(0, newest - count + 1);
   out.v.clear();
   for(int i = oldest; i <= newest; i++)
      out.v.push_back(b[(size_t)i].t);
   return (int)out.v.size();
  }
inline datetime iTime(const string &symbol, ENUM_TIMEFRAMES tf, int shift)
  {
   std::vector<Sim::Bar> b = Sim::BarsOf(tf);
   int i = (int)b.size() - 1 - shift;
   return i >= 0 && i < (int)b.size() ? b[(size_t)i].t : 0;
  }
inline double iClose(const string &symbol, ENUM_TIMEFRAMES tf, int shift)
  {
   std::vector<Sim::Bar> b = Sim::BarsOf(tf);
   int i = (int)b.size() - 1 - shift;
   return i >= 0 && i < (int)b.size() ? b[(size_t)i].c : 0;
  }
inline int iBarShift(const string &symbol, ENUM_TIMEFRAMES tf, datetime time, bool exact = false)
  {
   std::vector<Sim::Bar> b = Sim::BarsOf(tf);
   int found = -1;
   for(int i = (int)b.size() - 1; i >= 0; i--)
      if(b[(size_t)i].t <= time)
        {
         found = i;
         break;
        }
   if(found < 0)
      return -1;
   if(exact && Sim::Align(time, Sim::Seconds(tf)) != b[(size_t)found].t)
      return -1;
   return (int)b.size() - 1 - found;
  }
inline int PeriodSeconds(ENUM_TIMEFRAMES tf = PERIOD_CURRENT) { return Sim::Seconds(tf == PERIOD_CURRENT ? _Period : tf); }
inline datetime TimeCurrent() { return Sim::avail > 0 ? Sim::ticks[Sim::avail - 1].time : 0; }
inline datetime TimeTradeServer() { return Sim::wall; }
inline datetime TimeLocal() { return Sim::wall; }
inline double SymbolInfoDouble(const string &symbol, ENUM_SYMBOL_INFO_DOUBLE prop) { return prop == SYMBOL_POINT ? Sim::point : Sim::tickSize; }

// ---------------------------------------------------------------- chart and objects
inline long ChartGetInteger(long chart, ENUM_CHART_PROPERTY_INTEGER prop, int sub = 0) { return Sim::background; }
inline void ChartRedraw(long chart = 0) {}
inline bool ObjectCreate(long chart, const string &name, ENUM_OBJECT type, int sub, datetime t1, double p1, datetime t2 = 0, double p2 = 0)
  {
   if(Sim::objects.count(name.s))
      return false;
   Sim::Obj o;
   o.type = type;
   o.time[0] = t1;
   o.time[1] = t2;
   o.price[0] = p1;
   o.price[1] = p2;
   o.seq = ++Sim::objSeq;
   Sim::objects[name.s] = o;
   return true;
  }
inline int ObjectFind(long chart, const string &name) { return Sim::objects.count(name.s) ? 0 : -1; }
inline bool ObjectDelete(long chart, const string &name) { return Sim::objects.erase(name.s) > 0; }
inline int ObjectsDeleteAll(long chart, const string &prefix, int sub = -1, int type = -1)
  {
   int n = 0;
   for(auto it = Sim::objects.begin(); it != Sim::objects.end();)
      if(it->first.compare(0, prefix.s.size(), prefix.s) == 0)
        {
         it = Sim::objects.erase(it);
         n++;
        }
      else
         ++it;
   return n;
  }
inline Sim::Obj *MqlObj(const string &name)
  {
   auto it = Sim::objects.find(name.s);
   return it == Sim::objects.end() ? nullptr : &it->second;
  }
inline bool ObjectSetInteger(long chart, const string &name, ENUM_OBJECT_PROPERTY_INTEGER prop, int modifier, long value)
  {
   Sim::Obj *o = MqlObj(name);
   if(!o)
      return false;
   if(prop == OBJPROP_TIME)
      o->time[modifier & 1] = value;
   else
      o->ints[prop] = value;
   return true;
  }
inline bool ObjectSetInteger(long chart, const string &name, ENUM_OBJECT_PROPERTY_INTEGER prop, long value)
  {
   return ObjectSetInteger(chart, name, prop, 0, value);
  }
inline bool ObjectSetDouble(long chart, const string &name, ENUM_OBJECT_PROPERTY_DOUBLE prop, int modifier, double value)
  {
   Sim::Obj *o = MqlObj(name);
   if(!o)
      return false;
   o->price[modifier & 1] = value;
   return true;
  }
inline bool ObjectSetDouble(long chart, const string &name, ENUM_OBJECT_PROPERTY_DOUBLE prop, double value)
  {
   return ObjectSetDouble(chart, name, prop, 0, value);
  }
inline bool ObjectSetString(long chart, const string &name, ENUM_OBJECT_PROPERTY_STRING prop, const string &value)
  {
   Sim::Obj *o = MqlObj(name);
   if(!o)
      return false;
   (prop == OBJPROP_TEXT ? o->text : o->font) = value.s;
   return true;
  }

// ---------------------------------------------------------------- terminal functions
inline void Alert(const string &msg) { Sim::alerts.push_back({Sim::wall, msg.s}); }
inline void Print(const string &msg) { Sim::prints.push_back(msg.s); }
inline void Comment(const string &msg) { Sim::comment = msg.s; }
inline bool EventSetTimer(int seconds) { Sim::timerSeconds = seconds; return true; }
inline void EventKillTimer() { Sim::timerSeconds = 0; }
inline void ResetLastError() {}
inline int GetLastError() { return 0; }

// ---------------------------------------------------------------- strings
inline string IntegerToString(long v, int len = 0, ushort fill = ' ')
  {
   std::string s = std::to_string(v);
   if((int)s.size() < len)
      s = std::string((size_t)(len - (int)s.size()), (char)fill) + s;
   return string(s);
  }
inline string DoubleToString(double v, int digits = 8)
  {
   char buf[64];
   std::snprintf(buf, sizeof(buf), "%.*f", std::max(0, std::min(16, digits)), v);
   return string(std::string(buf));
  }
//--- MQL5 strings are UTF-16; here a character code becomes its UTF-8 form
inline string CharToString(uchar c)
  {
   std::string s;
   if(c < 0x80)
      s += (char)c;
   else
     {
      s += (char)(0xC0 | (c >> 6));
      s += (char)(0x80 | (c & 0x3F));
     }
   return string(s);
  }
inline int StringLen(const string &s) { return (int)s.s.size(); }
inline int StringFind(const string &s, const string &what, int start = 0)
  {
   size_t p = s.s.find(what.s, (size_t)std::max(0, start));
   return p == std::string::npos ? -1 : (int)p;
  }
inline string StringSubstr(const string &s, int start, int len = -1)
  {
   if(start < 0 || start >= (int)s.s.size())
      return string("");
   return string(s.s.substr((size_t)start, len < 0 ? std::string::npos : (size_t)len));
  }
inline ushort StringGetCharacter(const string &s, int pos)
  {
   return pos >= 0 && pos < (int)s.s.size() ? (ushort)(unsigned char)s.s[(size_t)pos] : 0;
  }

// ---------------------------------------------------------------- math
template <class A, class B> typename std::common_type<A, B>::type MathMax(A a, B b) { return a > b ? a : b; }
template <class A, class B> typename std::common_type<A, B>::type MathMin(A a, B b) { return a < b ? a : b; }
inline double MathFloor(double v) { return std::floor(v); }
inline double MathCeil(double v) { return std::ceil(v); }
inline double MathRound(double v) { return std::round(v); }
inline double MathSqrt(double v) { return std::sqrt(v); }
inline double MathAbs(double v) { return std::fabs(v); }
inline double MathPow(double a, double b) { return std::pow(a, b); }
// log10(0) would raise the floating-point zero-divide trap the simulation uses for "/ 0"
inline double MathLog10(double v) { return v > 0 ? std::log10(v) : (v == 0 ? -HUGE_VAL : NAN); }
