// Minimal MQL5 API surface for a syntax / type check of the IDCator MT5 sources with a C++ compiler.
// Not a runtime: functions are declared (or trivially defined) so that g++ -fsyntax-only can check names, argument
// counts and types. Only what the IDCator sources use is declared; a missing name is reported as an error.
#pragma once
#include <string>
#include <deque>
#include <cstdint>
#include <cmath>
#include <type_traits>

typedef unsigned char uchar;
typedef unsigned short ushort;
typedef unsigned int uint;
// ulong (unsigned long, 64-bit on Linux) comes from <sys/types.h>
typedef long long datetime;
typedef unsigned int color;

// ---------------------------------------------------------------- string
struct MqlString
  {
   std::string s;
   MqlString() {}
   MqlString(const char *c) : s(c) {}
   MqlString operator+(const MqlString &o) const { MqlString r; r.s = s + o.s; return r; }
   MqlString &operator+=(const MqlString &o) { s += o.s; return *this; }
   bool operator==(const MqlString &o) const { return s == o.s; }
   bool operator!=(const MqlString &o) const { return s != o.s; }
  };
typedef MqlString string;

// ---------------------------------------------------------------- dynamic arrays
template <class T>
struct MqlArr
  {
   std::deque<T> v;   // not vector: vector<bool> has no bool& element
   T &operator[](long i) { return v[(size_t)i]; }
   const T &operator[](long i) const { return v[(size_t)i]; }
  };
template <class T> int ArraySize(const MqlArr<T> &a) { return (int)a.v.size(); }
template <class T, size_t N> int ArraySize(const T (&)[N]) { return (int)N; }
template <class T> int ArrayResize(MqlArr<T> &a, int n, int reserve = 0) { a.v.resize((size_t)n); return n; }
template <class T> void ArrayFree(MqlArr<T> &a) { a.v.clear(); }
template <class T> bool ArrayRemove(MqlArr<T> &a, uint start, uint count = 0xFFFFFFFF) { return true; }
template <class T> int ArrayCopy(MqlArr<T> &dst, const MqlArr<T> &src, int dst_start = 0, int src_start = 0, int count = -1) { return 0; }
template <class T> int ArrayMaximum(const MqlArr<T> &a, int start = 0, int count = -1) { return 0; }
template <class T, class V> int ArrayInitialize(MqlArr<T> &a, V value) { return 0; }
template <class T> bool ArraySetAsSeries(MqlArr<T> &a, bool flag) { return true; }

// ---------------------------------------------------------------- enums and constants
enum ENUM_TIMEFRAMES { PERIOD_CURRENT = 0, PERIOD_M1 = 1, PERIOD_M2, PERIOD_M3, PERIOD_M5 = 5, PERIOD_M15 = 15, PERIOD_M30 = 30,
                       PERIOD_H1 = 16385, PERIOD_H4 = 16388, PERIOD_D1 = 16408, PERIOD_W1 = 32769, PERIOD_MN1 = 49153 };
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

// ---------------------------------------------------------------- predefined variables
extern string _Symbol;
extern ENUM_TIMEFRAMES _Period;
extern double _Point;
extern int _Digits;

// ---------------------------------------------------------------- functions
int CopyTicksRange(const string &symbol, MqlArr<MqlTick> &ticks, uint flags = COPY_TICKS_ALL, ulong from_msc = 0, ulong to_msc = 0);
int CopyTime(const string &symbol, ENUM_TIMEFRAMES tf, int start, int count, MqlArr<datetime> &out);
int Bars(const string &symbol, ENUM_TIMEFRAMES tf);
datetime iTime(const string &symbol, ENUM_TIMEFRAMES tf, int shift);
double iClose(const string &symbol, ENUM_TIMEFRAMES tf, int shift);
int iBarShift(const string &symbol, ENUM_TIMEFRAMES tf, datetime time, bool exact = false);
int PeriodSeconds(ENUM_TIMEFRAMES tf = PERIOD_CURRENT);
datetime TimeCurrent();
datetime TimeTradeServer();
datetime TimeLocal();
double SymbolInfoDouble(const string &symbol, ENUM_SYMBOL_INFO_DOUBLE prop);
long ChartGetInteger(long chart, ENUM_CHART_PROPERTY_INTEGER prop, int sub = 0);
void ChartRedraw(long chart = 0);
bool ObjectCreate(long chart, const string &name, ENUM_OBJECT type, int sub, datetime t1, double p1, datetime t2 = 0, double p2 = 0);
int ObjectFind(long chart, const string &name);
bool ObjectDelete(long chart, const string &name);
int ObjectsDeleteAll(long chart, const string &prefix, int sub = -1, int type = -1);
bool ObjectSetInteger(long chart, const string &name, ENUM_OBJECT_PROPERTY_INTEGER prop, long value);
bool ObjectSetInteger(long chart, const string &name, ENUM_OBJECT_PROPERTY_INTEGER prop, int modifier, long value);
bool ObjectSetDouble(long chart, const string &name, ENUM_OBJECT_PROPERTY_DOUBLE prop, double value);
bool ObjectSetDouble(long chart, const string &name, ENUM_OBJECT_PROPERTY_DOUBLE prop, int modifier, double value);
bool ObjectSetString(long chart, const string &name, ENUM_OBJECT_PROPERTY_STRING prop, const string &value);
void Alert(const string &msg);
void Print(const string &msg);
void Comment(const string &msg);
bool EventSetTimer(int seconds);
void EventKillTimer();
void ResetLastError();
int GetLastError();
string IntegerToString(long v, int len = 0, ushort fill = ' ');
string DoubleToString(double v, int digits = 8);
string CharToString(uchar c);
int StringLen(const string &s);
int StringFind(const string &s, const string &what, int start = 0);
string StringSubstr(const string &s, int start, int len = -1);
ushort StringGetCharacter(const string &s, int pos);

template <class A, class B> typename std::common_type<A, B>::type MathMax(A a, B b) { return a > b ? a : b; }
template <class A, class B> typename std::common_type<A, B>::type MathMin(A a, B b) { return a < b ? a : b; }
inline double MathFloor(double v) { return std::floor(v); }
inline double MathCeil(double v) { return std::ceil(v); }
inline double MathRound(double v) { return std::round(v); }
inline double MathSqrt(double v) { return std::sqrt(v); }
inline double MathAbs(double v) { return std::fabs(v); }
inline double MathPow(double a, double b) { return std::pow(a, b); }
inline double MathLog10(double v) { return std::log10(v); }

// MQL5 colour literal C'r,g,b'
inline constexpr color MqlRgb(int r, int g, int b) { return (color)((r & 255) | ((g & 255) << 8) | ((b & 255) << 16)); }
