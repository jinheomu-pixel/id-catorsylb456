//+------------------------------------------------------------------+
//| IDC Heavy Absorption (MetaTrader 5)                              |
//| Clean-room replica of the order-flow tool shown in ProfitPro's   |
//| videos; not their code. Not a buy/sell signal.                   |
//|                                                                  |
//| Marks exceptionally heavy aggressive volume at one price inside  |
//| a bar: green circle = buyers, red = sellers. Threshold = mean +  |
//| k standard deviations of the heaviest price per bar and side     |
//| over the lookback window. The number is the aggressive volume at |
//| that price and keeps counting up on the forming bar.             |
//| With exchange trades a single big print can be used instead.    |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "Heavy aggressive volume at one price, marked with a circle that grows with the volume."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpRowPoints    = 0;              // Row size in points (0 = auto)
input int              InpMaxBars      = 1500;           // Bars to load

#include <IDCator/HeavyAbsorption.mqh>

CHeavyAbsorption g_ind;

int OnInit()
  {
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.Init(InpRowPoints);
   EventSetTimer(1);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   ObjectsDeleteAll(0, HA_PFX);
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
      g_ind.SetRow(IdcAutoRow(high, low, rates_total, g_ind.tick));
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC Heavy Absorption: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
