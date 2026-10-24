//+------------------------------------------------------------------+
//| IDCator for MetaTrader 5 - shared core                           |
//|                                                                  |
//| * CIdcTickReader  - reads ticks (CopyTicksRange) and classifies  |
//|   them as aggressive buys or sells: exchange trade flags when    |
//|   the broker sends trades, otherwise the direction of the quote  |
//|   (CFD symbols: every quote change counts as 1).                 |
//| * CIdcFootprint   - buy/sell volume per price row of one bar.    |
//| * CIdcBarClock    - maps tick times to chart bars.               |
//| * CIdcWindowStats - rolling mean / standard deviation per bar.   |
//| * drawing helpers - rectangles, texts, lines; colours blended    |
//|   with the chart background (MT5 objects have no transparency).  |
//+------------------------------------------------------------------+
#ifndef IDCATOR_CORE_MQH
#define IDCATOR_CORE_MQH

enum ENUM_IDC_TICKSRC
  {
   IDC_TICKS_AUTO   = 0,   // Auto (trades when available, else quotes)
   IDC_TICKS_TRADES = 1,   // Exchange trades (Last + buy/sell flags)
   IDC_TICKS_QUOTES = 2    // Quote changes (CFD / forex)
  };

//--- one classified trade (or quote tick)
struct IdcTrade
  {
   long              msc;      // tick time in milliseconds
   double            price;
   double            volume;
   int               side;     // +1 aggressive buy, -1 aggressive sell
  };

//+------------------------------------------------------------------+
//| Reads new ticks and classifies them                              |
//+------------------------------------------------------------------+
class CIdcTickReader
  {
private:
   string            m_symbol;
   ENUM_IDC_TICKSRC  m_src;
   bool              m_trades;     // resolved: true = exchange trades, false = quotes
   bool              m_resolved;
   long              m_fromMsc;    // next read starts here (inclusive)
   int               m_seenAtFrom; // ticks with time == m_fromMsc already processed
   double            m_prevMid;
   double            m_prevLast;
   int               m_prevSide;

public:
                     CIdcTickReader(void) { m_symbol = ""; m_src = IDC_TICKS_AUTO; m_trades = false; m_resolved = false;
                                            m_fromMsc = 0; m_seenAtFrom = 0; m_prevMid = 0; m_prevLast = 0; m_prevSide = 0; }
   void              Init(const string symbol, const ENUM_IDC_TICKSRC src, const long fromMsc)
     {
      m_symbol = symbol;
      m_src = src;
      m_resolved = (src != IDC_TICKS_AUTO);
      m_trades = (src == IDC_TICKS_TRADES);
      m_fromMsc = fromMsc;
      m_seenAtFrom = 0;
      m_prevMid = 0;
      m_prevLast = 0;
      m_prevSide = 0;
     }
   bool              UsesTrades(void) const { return m_trades; }
   bool              Resolved(void) const { return m_resolved; }
   long              NextMsc(void) const { return m_fromMsc; }
   int               Read(const long toMsc, IdcTrade &out[]);
  };

//--- reads [m_fromMsc, toMsc]; returns the number of classified trades, -1 when the history is not ready yet
int CIdcTickReader::Read(const long toMsc, IdcTrade &out[])
  {
   ArrayResize(out, 0);
   if(toMsc < m_fromMsc)
      return 0;
   MqlTick ticks[];
   uint flags = COPY_TICKS_ALL;
   if(m_resolved)
      flags = m_trades ? COPY_TICKS_TRADE : COPY_TICKS_INFO;
   ResetLastError();
   int n = CopyTicksRange(m_symbol, ticks, flags, (ulong)m_fromMsc, (ulong)toMsc);
   if(n < 0)
      return -1;
   if(n == 0)
      return 0;

   if(!m_resolved)
     {
      // trades are available when the feed marks ticks as Last / Buy / Sell with a last price
      m_trades = false;
      for(int i = 0; i < n; i++)
         if((ticks[i].flags & (TICK_FLAG_LAST | TICK_FLAG_BUY | TICK_FLAG_SELL)) != 0 && ticks[i].last > 0)
           {
            m_trades = true;
            break;
           }
      m_resolved = true;
     }

   ArrayResize(out, n);
   int count = 0;
   int skip = m_seenAtFrom;
   long lastMsc = m_fromMsc;
   int atLast = m_seenAtFrom;
   for(int i = 0; i < n; i++)
     {
      long msc = ticks[i].time_msc;
      if(msc == m_fromMsc && skip > 0)
        {
         skip--;
         continue;
        }
      if(msc == lastMsc)
         atLast++;
      else
        {
         lastMsc = msc;
         atLast = 1;
        }

      int side = 0;
      double price = 0;
      double vol = ticks[i].volume_real > 0 ? ticks[i].volume_real : (double)ticks[i].volume;
      if(m_trades)
        {
         if((ticks[i].flags & (TICK_FLAG_LAST | TICK_FLAG_BUY | TICK_FLAG_SELL)) == 0 || ticks[i].last <= 0)
            continue;
         price = ticks[i].last;
         bool buy = (ticks[i].flags & TICK_FLAG_BUY) != 0;
         bool sell = (ticks[i].flags & TICK_FLAG_SELL) != 0;
         if(buy && !sell)
            side = 1;
         else
            if(sell && !buy)
               side = -1;
            else
               if(ticks[i].ask > 0 && price >= ticks[i].ask)
                  side = 1;
               else
                  if(ticks[i].bid > 0 && price <= ticks[i].bid)
                     side = -1;
                  else
                     side = price > m_prevLast ? 1 : (price < m_prevLast ? -1 : m_prevSide);
         m_prevLast = price;
        }
      else
        {
         // quotes only: the direction of the mid price says which side was aggressive, every change counts as 1
         if(ticks[i].bid <= 0 || ticks[i].ask <= 0)
            continue;
         double mid = (ticks[i].bid + ticks[i].ask) / 2.0;
         if(m_prevMid <= 0)
           {
            m_prevMid = mid;
            continue;
           }
         side = mid > m_prevMid ? 1 : (mid < m_prevMid ? -1 : m_prevSide);
         price = side > 0 ? ticks[i].ask : ticks[i].bid;
         m_prevMid = mid;
         if(vol <= 0)
            vol = 1;
        }
      if(side == 0)
         continue;
      if(vol <= 0)
         vol = 1;
      m_prevSide = side;
      out[count].msc = msc;
      out[count].price = price;
      out[count].volume = vol;
      out[count].side = side;
      count++;
     }
   ArrayResize(out, count);
   if(lastMsc == m_fromMsc)
      m_seenAtFrom = atLast;
   else
     {
      m_fromMsc = lastMsc;
      m_seenAtFrom = atLast;
     }
   return count;
  }

//+------------------------------------------------------------------+
//| Buy / sell volume per price row of one bar                       |
//+------------------------------------------------------------------+
class CIdcFootprint
  {
public:
   long              lowRow;    // row index of element 0 (row = floor(price / rowSize))
   double            rowSize;
   double            buy[];
   double            sell[];

                     CIdcFootprint(void) { lowRow = 0; rowSize = 1; }
   void              Clear(void) { lowRow = 0; ArrayResize(buy, 0); ArrayResize(sell, 0); }
   int               Rows(void) const { return ArraySize(buy); }
   bool              Empty(void) const { return ArraySize(buy) == 0; }
   long              RowOf(const double price) const { return (long)MathFloor(price / rowSize + 1e-7); }
   double            RowLow(const int i) const { return (double)(lowRow + i) * rowSize; }
   double            Total(const int i) const { return buy[i] + sell[i]; }
   void              Add(const double price, const double vol, const int side);
   double            TotalVolume(void) const
     {
      double t = 0;
      for(int i = 0; i < ArraySize(buy); i++)
         t += buy[i] + sell[i];
      return t;
     }
  };

void CIdcFootprint::Add(const double price, const double vol, const int side)
  {
   long row = RowOf(price);
   int n = ArraySize(buy);
   if(n == 0)
     {
      lowRow = row;
      ArrayResize(buy, 1);
      ArrayResize(sell, 1);
      buy[0] = 0;
      sell[0] = 0;
      n = 1;
     }
   if(row < lowRow)
     {
      int shift = (int)(lowRow - row);
      ArrayResize(buy, n + shift);
      ArrayResize(sell, n + shift);
      for(int i = n - 1; i >= 0; i--)
        {
         buy[i + shift] = buy[i];
         sell[i + shift] = sell[i];
        }
      for(int i = 0; i < shift; i++)
        {
         buy[i] = 0;
         sell[i] = 0;
        }
      lowRow = row;
      n += shift;
     }
   if(row >= lowRow + n)
     {
      int add = (int)(row - (lowRow + n)) + 1;
      ArrayResize(buy, n + add);
      ArrayResize(sell, n + add);
      for(int i = n; i < n + add; i++)
        {
         buy[i] = 0;
         sell[i] = 0;
        }
      n += add;
     }
   int k = (int)(row - lowRow);
   if(side > 0)
      buy[k] += vol;
   else
      sell[k] += vol;
  }

//+------------------------------------------------------------------+
//| Rolling statistics: one bucket (n, sum, sum of squares) per bar  |
//+------------------------------------------------------------------+
class CIdcWindowStats
  {
private:
   double            m_n[];
   double            m_s[];
   double            m_q[];
   int               m_len;
   double            m_tn;
   double            m_ts;
   double            m_tq;
   double            m_bn;
   double            m_bs;
   double            m_bq;

public:
                     CIdcWindowStats(void) { m_len = 100; Reset(); }
   void              Init(const int lookbackBars) { m_len = MathMax(1, lookbackBars); Reset(); }
   void              Reset(void) { ArrayResize(m_n, 0); ArrayResize(m_s, 0); ArrayResize(m_q, 0); m_tn = 0; m_ts = 0; m_tq = 0; m_bn = 0; m_bs = 0; m_bq = 0; }
   //--- samples of the bar being collected
   void              Add(const double v) { m_bn += 1; m_bs += v; m_bq += v * v; }
   //--- the bar is complete: it joins the window, the oldest bar leaves it
   void              CloseBar(void)
     {
      int k = ArraySize(m_n);
      ArrayResize(m_n, k + 1);
      ArrayResize(m_s, k + 1);
      ArrayResize(m_q, k + 1);
      m_n[k] = m_bn;
      m_s[k] = m_bs;
      m_q[k] = m_bq;
      m_tn += m_bn;
      m_ts += m_bs;
      m_tq += m_bq;
      m_bn = 0;
      m_bs = 0;
      m_bq = 0;
      if(ArraySize(m_n) > m_len)
        {
         int drop = ArraySize(m_n) - m_len;
         for(int i = 0; i < drop; i++)
           {
            m_tn -= m_n[i];
            m_ts -= m_s[i];
            m_tq -= m_q[i];
           }
         ArrayRemove(m_n, 0, drop);
         ArrayRemove(m_s, 0, drop);
         ArrayRemove(m_q, 0, drop);
        }
     }
   double            Count(void) const { return m_tn; }
   double            Mean(void) const { return m_tn > 0 ? m_ts / m_tn : 0; }
   double            StdDev(void) const
     {
      if(m_tn <= 0)
         return 0;
      double m = m_ts / m_tn;
      return MathSqrt(MathMax(0.0, m_tq / m_tn - m * m));
     }
   double            Threshold(const double k) const { return Mean() + k * StdDev(); }
  };

//+------------------------------------------------------------------+
//| Maps tick times to bars of the chart (time[] non-series)         |
//+------------------------------------------------------------------+
int IdcBarOf(const datetime &time[], const int rates_total, const long msc, const int hint)
  {
   datetime t = (datetime)(msc / 1000);
   int i = MathMax(0, MathMin(hint, rates_total - 1));
   if(time[i] > t)
     {
      // binary search below the hint
      int lo = 0, hi = i;
      while(lo < hi)
        {
         int mid = (lo + hi + 1) / 2;
         if(time[mid] <= t)
            lo = mid;
         else
            hi = mid - 1;
        }
      return time[lo] <= t ? lo : -1;
     }
   while(i + 1 < rates_total && time[i + 1] <= t)
      i++;
   return i;
  }

//+------------------------------------------------------------------+
//| Tick feed shared by all indicators: history first, then every    |
//| new tick; a bar is closed when the next bar's first trade comes, |
//| or 30 s after its end when the market goes quiet (timer).        |
//+------------------------------------------------------------------+
class CIdcFeed
  {
protected:
   CIdcTickReader    m_reader;
   int               m_curBar;       // non-series index of the forming bar (-1 none)
   datetime          m_curBarTime;
   int               m_closedBar;    // last bar handed to OnBarClose
   int               m_hint;
   long              m_liveFromMsc;  // trades at or after this time are realtime (alerts allowed)
   long              m_lastTradeMsc; // feed clock: last trade stamp ...
   datetime          m_lastArrival;  // ... and the local time it arrived
   bool              m_started;
   bool              m_loading;      // history ticks not available yet
   ENUM_IDC_TICKSRC  m_src;
   int               m_maxBars;

public:
                     CIdcFeed(void) { m_curBar = -1; m_curBarTime = 0; m_closedBar = -1; m_hint = 0; m_liveFromMsc = 0;
                                      m_lastTradeMsc = 0; m_lastArrival = 0; m_started = false; m_loading = false;
                                      m_src = IDC_TICKS_AUTO; m_maxBars = 1000; }
   void              Setup(const ENUM_IDC_TICKSRC src, const int maxBars) { m_src = src; m_maxBars = MathMax(10, maxBars); }
   bool              Loading(void) const { return m_loading; }
   bool              UsesTrades(void) const { return m_reader.UsesTrades(); }
   bool              Started(void) const { return m_started; }
   int               CurrentBar(void) const { return m_curBar; }
   virtual void      OnReset(void) {}
   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live) {}
   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live) {}
   virtual void      OnRefresh(const datetime &time[], const int rates_total) {}
   bool              Update(const int rates_total, const int prev_calculated, const datetime &time[]);
   void              Timer(void);
   //--- the feed's clock: last trade stamp + time since it arrived, never later than the server clock
   datetime          FeedNow(void) const
     {
      datetime server = TimeTradeServer();
      if(m_lastTradeMsc <= 0)
         return server;
      datetime feed = (datetime)(m_lastTradeMsc / 1000) + (TimeLocal() - m_lastArrival);
      return feed < server ? feed : server;
     }
  };

bool CIdcFeed::Update(const int rates_total, const int prev_calculated, const datetime &time[])
  {
   if(rates_total < 2)
      return false;
   if(prev_calculated == 0 || !m_started)
     {
      // (re)start: history changed or first call
      OnReset();
      int first = MathMax(0, rates_total - m_maxBars);
      m_reader.Init(_Symbol, m_src, (long)time[first] * 1000);
      m_curBar = -1;
      m_curBarTime = 0;
      m_closedBar = first - 1;
      m_hint = first;
      m_liveFromMsc = (long)TimeCurrent() * 1000;
      m_lastTradeMsc = 0;
      m_started = true;
     }
   long toMsc = (long)MathMax(TimeCurrent(), TimeTradeServer()) * 1000 + 60000;
   IdcTrade tr[];
   int n = m_reader.Read(toMsc, tr);
   if(n < 0)
     {
      m_loading = true;
      return false;
     }
   m_loading = false;
   for(int i = 0; i < n; i++)
     {
      int bar = IdcBarOf(time, rates_total, tr[i].msc, m_hint);
      if(bar < 0 || bar <= m_closedBar)
         continue;   // before the loaded window, or a late print of a bar that is already closed
      m_hint = bar;
      bool live = tr[i].msc >= m_liveFromMsc;
      if(bar != m_curBar)
        {
         if(m_curBar >= 0 && m_curBar > m_closedBar)
           {
            m_closedBar = m_curBar;
            OnBarClose(m_curBar, time, rates_total, live);
           }
         m_curBar = bar;
         m_curBarTime = time[bar];
        }
      OnTrade(bar, tr[i], time, rates_total, live);
      m_lastTradeMsc = tr[i].msc;
      m_lastArrival = TimeLocal();
     }
   OnRefresh(time, rates_total);
   return true;
  }

//--- quiet market: close the forming bar 30 s after its end on the feed clock
void CIdcFeed::Timer(void)
  {
   if(!m_started || m_curBar < 0 || m_curBar <= m_closedBar)
      return;
   if(m_curBarTime + PeriodSeconds() + 30 > FeedNow())
      return;
   datetime time[];
   int total = Bars(_Symbol, _Period);
   if(total <= 0 || CopyTime(_Symbol, _Period, 0, total, time) != total)
      return;
   // CopyTime returns oldest first, like the non-series time[] of OnCalculate
   if(m_curBar >= total || time[m_curBar] != m_curBarTime)
      return;
   m_closedBar = m_curBar;
   OnBarClose(m_curBar, time, total, true);
   OnRefresh(time, total);
   ChartRedraw();
  }

//+------------------------------------------------------------------+
//| Formatting                                                       |
//+------------------------------------------------------------------+
//--- "6.0" -> "6", "6.5" -> "6.5"
string IdcTrim(const string s)
  {
   int dot = StringFind(s, ".");
   if(dot < 0)
      return s;
   int end = StringLen(s);
   while(end > dot + 1 && StringGetCharacter(s, end - 1) == '0')
      end--;
   if(end == dot + 1)
      end = dot;
   return StringSubstr(s, 0, end);
  }

//--- v rounded half away from zero to `digits` decimals (not left to DoubleToString), without trailing zeros
string IdcNum(const double v, const int digits)
  {
   double f = MathPow(10, digits);
   return IdcTrim(DoubleToString(MathRound(v * f) / f, digits));
  }

string IdcVolText(const double v, const bool upperK)
  {
   double a = MathAbs(v);
   string k = upperK ? "K" : "k";
   if(a >= 1000000000)
      return IdcNum(v / 1000000000.0, 1) + "B";
   if(a >= 10000000)
      return IdcNum(v / 1000000.0, 0) + "M";
   if(a >= 1000000)
      return IdcNum(v / 1000000.0, 1) + "M";
   if(a >= 10000)
      return IdcNum(v / 1000.0, 0) + k;
   if(a >= 1000)
      return IdcNum(v / 1000.0, 1) + k;
   if(a >= 100)
      return IdcNum(v, 0);
   // crypto volumes are fractional: 18.5, 2.35, 0.042
   if(a >= 10)
      return IdcNum(v, 1);
   if(a >= 1)
      return IdcNum(v, 2);
   return IdcNum(v, 3);
  }

string IdcRatioText(const double r)
  {
   if(r >= 999)
      return "999x";
   return IdcNum(r, 1) + "x";
  }

//+------------------------------------------------------------------+
//| Colours: MT5 objects are opaque, so "transparency" is a blend    |
//| with the chart background                                        |
//+------------------------------------------------------------------+
color IdcRgb(const int r, const int g, const int b)
  {
   return (color)((r & 255) | ((g & 255) << 8) | ((b & 255) << 16));
  }

int IdcR(const color c) { return (int)(c & 255); }
int IdcG(const color c) { return (int)((c >> 8) & 255); }
int IdcB(const color c) { return (int)((c >> 16) & 255); }

//--- alpha 0..1 of the colour over the chart background
color IdcBlend(const color c, const double alpha)
  {
   color bg = (color)ChartGetInteger(0, CHART_COLOR_BACKGROUND);
   double a = MathMax(0.0, MathMin(1.0, alpha));
   int r = (int)MathRound(IdcR(bg) + (IdcR(c) - IdcR(bg)) * a);
   int g = (int)MathRound(IdcG(bg) + (IdcG(c) - IdcG(bg)) * a);
   int b = (int)MathRound(IdcB(bg) + (IdcB(c) - IdcB(bg)) * a);
   return IdcRgb(r, g, b);
  }

//+------------------------------------------------------------------+
//| Drawing helpers (object created on first use, then updated)      |
//+------------------------------------------------------------------+
void IdcRect(const string name, const datetime t1, const double p1, const datetime t2, const double p2, const color fill)
  {
   if(ObjectFind(0, name) < 0)
     {
      ObjectCreate(0, name, OBJ_RECTANGLE, 0, t1, p1, t2, p2);
      ObjectSetInteger(0, name, OBJPROP_FILL, true);
      ObjectSetInteger(0, name, OBJPROP_BACK, true);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
     }
   ObjectSetInteger(0, name, OBJPROP_TIME, 0, t1);
   ObjectSetDouble(0, name, OBJPROP_PRICE, 0, p1);
   ObjectSetInteger(0, name, OBJPROP_TIME, 1, t2);
   ObjectSetDouble(0, name, OBJPROP_PRICE, 1, p2);
   ObjectSetInteger(0, name, OBJPROP_COLOR, fill);
  }

void IdcText(const string name, const datetime t, const double p, const string text, const color clr, const int size,
             const string font, const ENUM_ANCHOR_POINT anchor, const bool back)
  {
   if(ObjectFind(0, name) < 0)
     {
      ObjectCreate(0, name, OBJ_TEXT, 0, t, p);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
     }
   ObjectSetInteger(0, name, OBJPROP_TIME, 0, t);
   ObjectSetDouble(0, name, OBJPROP_PRICE, 0, p);
   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetString(0, name, OBJPROP_FONT, font);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, size);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_ANCHOR, anchor);
   ObjectSetInteger(0, name, OBJPROP_BACK, back);
  }

void IdcLine(const string name, const datetime t1, const double p1, const datetime t2, const double p2, const color clr,
             const int width, const ENUM_LINE_STYLE style, const bool rayRight)
  {
   if(ObjectFind(0, name) < 0)
     {
      ObjectCreate(0, name, OBJ_TREND, 0, t1, p1, t2, p2);
      ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
      ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
      ObjectSetInteger(0, name, OBJPROP_BACK, true);
     }
   ObjectSetInteger(0, name, OBJPROP_TIME, 0, t1);
   ObjectSetDouble(0, name, OBJPROP_PRICE, 0, p1);
   ObjectSetInteger(0, name, OBJPROP_TIME, 1, t2);
   ObjectSetDouble(0, name, OBJPROP_PRICE, 1, p2);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, width);
   ObjectSetInteger(0, name, OBJPROP_STYLE, style);
   ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT, rayRight);
   ObjectSetInteger(0, name, OBJPROP_RAY_LEFT, false);
  }

//--- Wingdings symbols used as scalable shapes
#define IDC_WD_CIRCLE   108   // filled circle
#define IDC_WD_RING     161   // hollow circle
#define IDC_WD_SQUARE   111   // hollow square

string IdcGlyph(const int code)
  {
   return CharToString((uchar)code);
  }

//--- a round price step near px: 1, 2, 2.5 or 5 times a power of ten
double IdcNiceStep(const double px)
  {
   if(px <= 0)
      return 0;
   double mag = MathPow(10, MathFloor(MathLog10(px)));
   double f = px / mag;
   return (f < 1.5 ? 1.0 : f < 2.25 ? 2.0 : f < 3.5 ? 2.5 : f < 7.5 ? 5.0 : 10.0) * mag;
  }

//--- automatic row size: about 20 rows across the median closed bar of the chart, a whole number of ticks
//--- (1 tick on NQ M1, a few dollars on Bitcoin, works the same on CFDs, forex and any coin)
double IdcAutoRow(const double &high[], const double &low[], const int rates_total, const double tick)
  {
   int n = MathMin(200, rates_total - 1);
   if(n < 5 || tick <= 0)
      return tick;
   double r[];
   ArrayResize(r, n);
   for(int i = 0; i < n; i++)
      r[i] = high[rates_total - 2 - i] - low[rates_total - 2 - i];
   ArraySort(r);
   return MathMax(1, MathRound(IdcNiceStep(r[n / 2] / 20) / tick)) * tick;
  }

//--- price row size: input in points, 0 = one tick (the indicators replace it with IdcAutoRow once bars are there)
double IdcRowSize(const string symbol, const int rowPoints)
  {
   double tick = SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE);
   double point = SymbolInfoDouble(symbol, SYMBOL_POINT);
   if(tick <= 0)
      tick = point;
   if(rowPoints > 0)
      return MathMax(tick, rowPoints * point);
   return tick;
  }

#endif
