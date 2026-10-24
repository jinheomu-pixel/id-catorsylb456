//+------------------------------------------------------------------+
//| IDC Pressure Toolkit (MetaTrader 5)                              |
//| Clean-room replica of the order-flow tool shown in ProfitPro's   |
//| material; not their code. Not a buy/sell signal.                 |
//|                                                                  |
//| Pressure bubbles (dim = weak, bright = strong), big trades       |
//| (outlined square + volume), absorption dots at wicks (red above, |
//| green below, bigger = enhanced), absorption lines extended until |
//| price returns, psychological levels.                             |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "Pressure bubbles, big trades, absorption dots/lines and psychological levels (tick footprint)."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpRowPoints    = 0;              // Row size in points (0 = auto)
input int              InpMaxBars      = 1500;           // Bars to load

#include <IDCator/PressureToolkit.mqh>

CPressureToolkit g_ind;

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
   ObjectsDeleteAll(0, PTK_PFX);
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
      Comment(g_ind.Loading() ? "IDC Pressure Toolkit: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
