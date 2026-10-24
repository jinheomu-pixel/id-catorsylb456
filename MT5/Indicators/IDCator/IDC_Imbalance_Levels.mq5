//+------------------------------------------------------------------+
//| IDC Imbalance Levels (MetaTrader 5)                              |
//| Clean-room replica of the order-flow tool shown in ProfitPro's   |
//| videos; not their code. Not a buy/sell signal.                   |
//|                                                                  |
//| Stacked diagonal imbalances of every closed bar become support   |
//| (buy stack below the close) and resistance (sell stack above the |
//| close) zones, extended until price trades back into them.        |
//| Label: strongest ratio | stack volume. Darker = stronger.        |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "Stacked bid/ask imbalances as support/resistance zones until touched (tick footprint)."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

enum ENUM_IDC_ZERO
  {
   IDC_ZERO_MINRATIO = 0, // Count as min ratio
   IDC_ZERO_ONE      = 1, // Count as 1
   IDC_ZERO_SKIP     = 2  // Skip
  };

input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpRowPoints    = 0;              // Row size in points (0 = auto)
input int              InpMaxBars      = 1500;           // Bars to load
input group "Detection"
input double           InpMinRatio     = 5.0;            // Min imbalance ratio
input int              InpMinStack     = 3;              // Min stacked levels
input double           InpMinLevelVol  = 0;              // Min volume per level
input double           InpMinStackVol  = 0;              // Min stack volume
input ENUM_IDC_ZERO    InpZero         = IDC_ZERO_MINRATIO; // Zero-value handling
input bool             InpBeyondClose  = true;           // Buy stacks below close, sell stacks above
input bool             InpDiscardNext  = false;          // Discard if next bar touches
input group "Display"
input color            InpSupColor     = C'34,171,92';   // Support (buy stack)
input color            InpResColor     = C'233,57,61';   // Resistance (sell stack)
input int              InpMinOpacity   = 10;             // Opacity at min ratio %
input int              InpMaxOpacity   = 45;             // Opacity at full ratio %
input double           InpFullRatio    = 15;             // Full-opacity ratio
input bool             InpShowLabels   = true;           // Show labels
input color            InpTextColor    = C'224,224,224'; // Label color
input int              InpTextSize     = 8;              // Label size
input bool             InpKeepTouched  = true;           // Keep touched zones
input int              InpMaxZones     = 300;            // Max zones on chart
input group "Alerts"
input bool             InpAlertTouch   = true;           // Alert when price touches a zone

#define PFX "IDCimb_"

struct ImbZone
  {
   int               id;
   int               startBar;
   int               endBar;     // -1 while active
   bool              support;
   double            bottom;     // row low of the lowest level
   double            top;        // row high of the highest level
   double            ratio;
   double            volume;
  };

class CImbalance : public CIdcFeed
  {
public:
   CIdcFootprint     fp;
   ImbZone           zones[];
   int               nextId;
   double            rowSize;
   double            tick;

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, PFX);
      ArrayResize(zones, 0);
      nextId = 0;
      fp.Clear();
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      fp.Add(t.price, t.volume, t.side);
      // a trade inside an active zone touches it on that tick
      string msg = "";
      for(int i = ArraySize(zones) - 1; i >= 0; i--)
        {
         if(zones[i].endBar >= 0 || bar <= zones[i].startBar)
            continue;
         // the zone spans its rows: from the lowest row's low up to the highest row's last tick
         if(t.price < zones[i].bottom - tick * 0.25 || t.price > zones[i].top - tick * 0.75)
            continue;
         if(InpDiscardNext && bar == zones[i].startBar + 1)
           {
            DeleteZone(i);
            continue;
           }
         zones[i].endBar = bar;
         if(!InpKeepTouched)
           {
            msg += ZoneText(zones[i]) + "  ";
            DeleteZone(i);
            continue;
           }
         Draw(i, time, rates_total);
         msg += (zones[i].support ? "support " : "resistance ") + DoubleToString(zones[i].bottom, _Digits) + "-" +
                DoubleToString(zones[i].top - tick, _Digits) + " (" + ZoneText(zones[i]) + ")  ";
        }
      if(live && InpAlertTouch && StringLen(msg) > 0)
         Alert(_Symbol + " touched imbalance " + msg);
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      if(fp.Empty())
         return;
      double close = iClose(_Symbol, _Period, iBarShift(_Symbol, _Period, time[bar]));
      int n = fp.Rows();
      // buy stacks: row i buy vs row i-1 sell (diagonal)
      int runStart = -1;
      double runMax = 0, runVol = 0;
      for(int i = 1; i <= n; i++)
        {
         double r = i < n ? LevelRatio(fp.buy[i], fp.sell[i - 1]) : -1;
         if(r >= InpMinRatio * (1 - 1e-9))   // fractional (crypto) volumes: a ratio of exactly 5 may sum to 4.9999999
           {
            if(runStart < 0)
              {
               runStart = i;
               runMax = r;
               runVol = 0;
              }
            runMax = MathMax(runMax, r);
            runVol += fp.buy[i];
           }
         else
            if(runStart >= 0)
              {
               int runEnd = i - 1;
               double bottom = fp.RowLow(runStart), top = fp.RowLow(runEnd) + rowSize;
               bool okSide = !InpBeyondClose || top - tick * 0.5 < close;   // the whole stack below the close
               if(runEnd - runStart + 1 >= InpMinStack && runVol >= InpMinStackVol && okSide)
                  AddZone(bar, true, bottom, top, runMax, runVol, time, rates_total);
               runStart = -1;
              }
        }
      // sell stacks: row i sell vs row i+1 buy
      runStart = -1;
      for(int i = 0; i < n; i++)
        {
         double r = i < n - 1 ? LevelRatio(fp.sell[i], fp.buy[i + 1]) : -1;
         if(r >= InpMinRatio * (1 - 1e-9))   // fractional (crypto) volumes: a ratio of exactly 5 may sum to 4.9999999
           {
            if(runStart < 0)
              {
               runStart = i;
               runMax = r;
               runVol = 0;
              }
            runMax = MathMax(runMax, r);
            runVol += fp.sell[i];
           }
         else
            if(runStart >= 0)
              {
               int runEnd = i - 1;
               double bottom = fp.RowLow(runStart), top = fp.RowLow(runEnd) + rowSize;
               bool okSide = !InpBeyondClose || bottom > close + tick * 0.5;
               if(runEnd - runStart + 1 >= InpMinStack && runVol >= InpMinStackVol && okSide)
                  AddZone(bar, false, bottom, top, runMax, runVol, time, rates_total);
               runStart = -1;
              }
        }
      fp.Clear();
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      for(int i = 0; i < ArraySize(zones); i++)
         if(zones[i].endBar < 0)
            Draw(i, time, rates_total);
     }

   //--- ratio of an aggressive level against its diagonal opposite, -1 when the level does not count
   double            LevelRatio(const double aggressive, const double opposite)
     {
      if(aggressive <= 0 || aggressive < InpMinLevelVol)
         return -1;
      if(opposite > 0)
         return aggressive / opposite;
      if(InpZero == IDC_ZERO_MINRATIO)
         return InpMinRatio;
      if(InpZero == IDC_ZERO_ONE)
         return aggressive;
      return -1;
     }

   string            ZoneText(const ImbZone &z) { return IdcRatioText(z.ratio) + " | " + IdcVolText(z.volume, false); }

   void              AddZone(const int bar, const bool support, const double bottom, const double top, const double ratio,
                             const double volume, const datetime &time[], const int rates_total)
     {
      int k = ArraySize(zones);
      ArrayResize(zones, k + 1);
      zones[k].id = nextId++;
      zones[k].startBar = bar;
      zones[k].endBar = -1;
      zones[k].support = support;
      zones[k].bottom = bottom;
      zones[k].top = top;
      zones[k].ratio = ratio;
      zones[k].volume = volume;
      Draw(k, time, rates_total);
      if(ArraySize(zones) > InpMaxZones)
        {
         // drop the oldest touched zone first, then the oldest one
         int drop = 0;
         for(int i = 0; i < ArraySize(zones); i++)
            if(zones[i].endBar >= 0)
              {
               drop = i;
               break;
              }
         DeleteZone(drop);
        }
     }

   void              DeleteZone(const int i)
     {
      ObjectDelete(0, PFX + "z" + IntegerToString(zones[i].id));
      ObjectDelete(0, PFX + "t" + IntegerToString(zones[i].id));
      ArrayRemove(zones, i, 1);
     }

   void              Draw(const int i, const datetime &time[], const int rates_total)
     {
      int last = zones[i].endBar >= 0 ? zones[i].endBar : rates_total - 1;
      if(last >= rates_total)
         last = rates_total - 1;
      datetime t1 = time[zones[i].startBar];
      datetime t2 = time[last] + PeriodSeconds();
      double span = MathMax(0.0001, InpFullRatio - InpMinRatio);
      double f = MathMax(0.0, MathMin(1.0, (zones[i].ratio - InpMinRatio) / span));
      double opacity = (InpMinOpacity + (InpMaxOpacity - InpMinOpacity) * f) / 100.0;
      color base = zones[i].support ? InpSupColor : InpResColor;
      string id = IntegerToString(zones[i].id);
      IdcRect(PFX + "z" + id, t1, zones[i].top, t2, zones[i].bottom, IdcBlend(base, opacity));
      if(InpShowLabels)
         IdcText(PFX + "t" + id, t2, (zones[i].top + zones[i].bottom) / 2, ZoneText(zones[i]), InpTextColor, InpTextSize,
                 "Arial Bold", ANCHOR_RIGHT, false);
     }
  };

CImbalance g_ind;

int OnInit()
  {
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.rowSize = IdcRowSize(_Symbol, InpRowPoints);
   g_ind.tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(g_ind.tick <= 0)
      g_ind.tick = _Point;
   g_ind.fp.rowSize = g_ind.rowSize;
   g_ind.nextId = 0;
   EventSetTimer(1);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   ObjectsDeleteAll(0, PFX);
   Comment("");
  }

void OnTimer()
  {
   g_ind.Timer();
  }

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
  {
   // automatic row size from the chart's bars, set at every (re)start
   if(prev_calculated == 0 && InpRowPoints <= 0)
     {
      g_ind.rowSize = IdcAutoRow(high, low, rates_total, g_ind.tick);
      g_ind.fp.rowSize = g_ind.rowSize;
     }
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC Imbalance: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
