// ==========================================================================
//  MainWindow.xaml.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ==========================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using QScalp.Connector;
using QScalp.Windows;

namespace QScalp
{
  public sealed partial class MainWindow : Window
  {
    // **********************************************************************

    TermManager tmgr;
    DataProvider dp;

    HashSet<Key> pressedKeys;
    DispatcherTimer sbUpdater;

    ConfigWindow cfgw;
    TradeLogWindow tlw;

    // **********************************************************************

    public MainWindow()
    {
      InitializeComponent();

      this.Title = cfg.MainFormTitle;
      this.Topmost = cfg.u.WindowTopmost;

      InitMenuTips();
      LoadWindowState();

      // ------------------------------------------------------------

      sv.OnQuoteClick = QuoteClick;

      // ------------------------------------------------------------

      tmgr = new TermManager(sv);
      dp = new DataProvider(sv, tmgr);

      // ------------------------------------------------------------

      pressedKeys = new HashSet<Key>();

      // ------------------------------------------------------------

      sbUpdater = new DispatcherTimer();
      sbUpdater.Interval = cfg.SbUpdateInterval;
      sbUpdater.Tick += new EventHandler(SbUpdaterTick);

      Loaded += new RoutedEventHandler(MainWindow_Loaded);
    }

    // **********************************************************************

    void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
      tmgr.Connect();
      dp.Connect();
      
      // Устанавливаем callback для очистки визуализации при перемотке
      dp.SetClearVisualizationCallback(() => 
      {
        Dispatcher.Invoke(() => sv.ClearAllData());
      });

      SbUpdaterTick(sender, e);
      UpdateWorkSize(0);

      InitTradeLogWindow();
      InitPlaybackMenu();

      sbUpdater.Start();

      this.Activate();
    }

    // **********************************************************************

    void InitPlaybackMenu()
    {
      // Показываем/скрываем меню воспроизведения в зависимости от режима
      menuPlayback.Visibility = dp.IsHistoricalMode 
        ? System.Windows.Visibility.Visible 
        : System.Windows.Visibility.Collapsed;
      
      if(dp.IsHistoricalMode)
      {
        UpdatePlaybackMenu();
      }
    }

    // **********************************************************************

    //protected override void OnActivated(EventArgs e)
    //{
    //  this.Focus();
    //  base.OnActivated(e);
    //}

    // **********************************************************************

    void LoadWindowState()
    {
      this.Left = cfg.u.WindowLeft;
      this.Top = cfg.u.WindowTop;
      this.Width = cfg.u.WindowWidth;
      this.Height = cfg.u.WindowHeight;

      this.WindowState = cfg.u.WindowState;

      Program.FixWindowLocation(this);
    }

    // **********************************************************************

    void SaveWindowState()
    {
      cfg.u.WindowState = WindowState;

      cfg.u.WindowLeft = RestoreBounds.Left;
      cfg.u.WindowTop = RestoreBounds.Top;
      cfg.u.WindowWidth = RestoreBounds.Width;
      cfg.u.WindowHeight = RestoreBounds.Height;
    }

    // **********************************************************************

    protected override void OnClosing(CancelEventArgs e)
    {
      if(cfg.u.ConfirmExit && MessageBox.Show("Выйти из программы?", cfg.ProgName,
        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
      {
        e.Cancel = true;
      }
      else
      {
        SaveWindowState();
      }

      base.OnClosing(e);
    }

    // **********************************************************************

    protected override void OnClosed(EventArgs e)
    {
      dp.Disconnect();
      tmgr.Disconnect();

      if(cfg.u.TradeLogFlush)
      {
        tmgr.Position.TradeLog.Commit();
        tmgr.Position.TradeLog.Clear();
        tmgr.Position.TradeLog.Flush(cfg.TradeLogFile);
      }

      base.OnClosed(e);
    }

    // **********************************************************************
  }
}
