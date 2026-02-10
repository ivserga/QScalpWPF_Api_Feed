// =======================================================================
//    Handlers.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

using System.Windows.Input;
using QScalp.XMedia;

namespace QScalp
{
  partial class MainWindow
  {
    // **********************************************************************
    // *                       Обработка клавиатуры                         *
    // **********************************************************************

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
      if(IsKeyboardFocused)
      {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch(key)
        {
          // --------------------------------------------------------

          case cfg.FKeySaveConf:
            MenuSaveConf_Click(this, null);
            break;
          case cfg.FKeyLoadConf:
            MenuLoadConf_Click(this, null);
            break;

          case cfg.FKeyCfgOrExit:
            if((e.KeyboardDevice.Modifiers & ModifierKeys.Alt)
              == ModifierKeys.None)
              MenuSettings_Click(this, null);
            else
              MenuExit_Click(this, null);
            break;

          case cfg.FKeyTradeLog:
            MenuTradeLog_Click(this, null);
            break;

          case cfg.FKeyDropPos:
            MenuDropPos_Click(this, null);
            break;
          case cfg.FKeyClearGuide:
            MenuClearGuide_Click(this, null);
            break;
          case cfg.FKeyClearLevels:
            MenuClearLevels_Click(this, null);
            break;

          case cfg.FKeyShowMenu:
            menu.IsSubmenuOpen = true;
            menu.Focus();
            break;

          // --------------------------------------------------------

          default:
            // Управление воспроизведением в историческом режиме
            if(dp.IsHistoricalMode && dp.Playback != null && HandlePlaybackKey(key, e.KeyboardDevice.Modifiers))
            {
              // Клавиша обработана
            }
            else if(key == cfg.u.KeyCenterSpread)
              sv.CenterSpread();

            else if(key == cfg.u.KeyPageUp)
              sv.Page(1);
            else if(key == cfg.u.KeyPageDown)
              sv.Page(-1);

            else if(key == cfg.u.KeyWorkSizeInc)
            {
              if(!e.IsRepeat)
                UpdateWorkSize(cfg.u.WorkSizeDelta);
            }
            else if(key == cfg.u.KeyWorkSizeDec)
            {
              if(!e.IsRepeat)
                UpdateWorkSize(-cfg.u.WorkSizeDelta);
            }

            else if(!pressedKeys.Contains(key))
            {
              TryExecAction(cfg.u.KeyDownBindings, key);
              pressedKeys.Add(key);
            }

            break;

          // --------------------------------------------------------
        }

        UpdateKeyStatus();
        e.Handled = true;
      }
      else
        base.OnPreviewKeyDown(e);
    }

    // **********************************************************************

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
      if(IsKeyboardFocused)
      {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        TryExecAction(cfg.u.KeyUpBindings, key);
        pressedKeys.Remove(key);

        UpdateKeyStatus();
        e.Handled = true;
      }
      else
        base.OnPreviewKeyUp(e);
    }

    // **********************************************************************

    void TryExecAction(KeyBindings bindings, Key key)
    {
      OwnAction[] actions;

      if(!pressedKeys.Contains(cfg.u.KeyBlockKey)
        && bindings.TryGetValue(key, out actions))
      {
        for(int i = 0; i < actions.Length; i++)
        {
          OwnAction a = actions[i];

          if(a.Quote == BaseQuote.Absolute)
          {
            if(sv.SelectedPrice != 0)
            {
              a.Value = sv.SelectedPrice;
              tmgr.ExecAction(a);
            }
          }
          else
            tmgr.ExecAction(a);
        }
      }
    }

    // **********************************************************************
    // *                          Обработка мыши                            *
    // **********************************************************************

    void QuoteClick(MouseButtonEventArgs e, int price)
    {
      switch(e.ChangedButton)
      {
        // ----------------------------------------------

        case MouseButton.Left:

          if(cfg.u.MouseEnabled && tmgr.AskPrice > tmgr.BidPrice)
          {
            if(price >= tmgr.AskPrice)
              tmgr.ExecAction(new OwnAction(
                TradeOp.Sell,
                BaseQuote.Absolute,
                price,
                cfg.u.WorkSize));

            else if(price <= tmgr.BidPrice)
              tmgr.ExecAction(new OwnAction(
                TradeOp.Buy,
                BaseQuote.Absolute,
                price,
                cfg.u.WorkSize));
          }

          break;

        // ----------------------------------------------

        case MouseButton.Middle:

          if(cfg.u.MouseEnabled && tmgr.AskPrice > tmgr.BidPrice)
          {
            if(price >= tmgr.AskPrice)
              tmgr.CreateStopOrder(
                price,
                price + cfg.u.MouseSlippage,
                cfg.u.WorkSize);

            else if(price <= tmgr.BidPrice)
              tmgr.CreateStopOrder(
                price,
                price - cfg.u.MouseSlippage,
                -cfg.u.WorkSize);
          }

          break;

        // ----------------------------------------------

        case MouseButton.Right:

          tmgr.ExecAction(new OwnAction(
            TradeOp.Cancel,
            BaseQuote.Absolute,
            price,
            0));

          break;

        // ----------------------------------------------
      }
    }

    // **********************************************************************

    void WorkSizeStatusClick(object sender, MouseButtonEventArgs e)
    {
      switch(e.ChangedButton)
      {
        case MouseButton.Left:
          UpdateWorkSize(cfg.u.WorkSizeDelta);
          break;

        case MouseButton.Right:
          UpdateWorkSize(-cfg.u.WorkSizeDelta);
          break;
      }
    }

    // **********************************************************************

    void KeyStatusClick(object sender, MouseButtonEventArgs e)
    {
      if(e.ChangedButton == MouseButton.Left && e.ClickCount > 1)
      {
        MenuSettings_Click(sender, e);
        cfgw.ShowKeyBindings();
      }
    }

    // **********************************************************************

    void PosStatusClick(object sender, MouseButtonEventArgs e)
    {
      if(e.ChangedButton == MouseButton.Left && e.ClickCount > 1)
      {
        MenuSettings_Click(sender, e);
        cfgw.ShowPosition();
      }
    }

    // **********************************************************************

    void ResultStatusClick(object sender, MouseButtonEventArgs e)
    {
      if(e.ChangedButton == MouseButton.Left && e.ClickCount > 1)
        MenuTradeLog_Click(sender, e);
    }

    // **********************************************************************
    // *                    Управление воспроизведением                     *
    // **********************************************************************

    bool HandlePlaybackKey(Key key, ModifierKeys modifiers)
    {
      bool hasCtrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
      
      switch(key)
      {
        case Key.Space:
          // Пробел - пауза/воспроизведение
          if(dp.Playback.IsPlaying)
          {
            if(dp.Playback.IsPaused)
              dp.Playback.Start();
            else
              dp.Playback.Pause();
          }
          else
          {
            dp.Playback.Start();
          }
          return true;
          
        case Key.Left:
          // Стрелка влево - перемотка назад
          if(hasCtrl)
            dp.Playback.SeekBackward(60);  // 1 минута
          else
            dp.Playback.SeekBackward(10);  // 10 секунд
          return true;
          
        case Key.Right:
          // Стрелка вправо - перемотка вперёд
          if(hasCtrl)
            dp.Playback.SeekForward(60);   // 1 минута
          else
            dp.Playback.SeekForward(10);   // 10 секунд
          return true;
          
        case Key.Home:
          // Home - в начало
          dp.Playback.SeekToStart();
          return true;
          
        case Key.OemPlus:
        case Key.Add:
          // + увеличить скорость
          IncreasePlaybackSpeed();
          return true;
          
        case Key.OemMinus:
        case Key.Subtract:
          // - уменьшить скорость
          DecreasePlaybackSpeed();
          return true;
      }
      
      return false;
    }

    // **********************************************************************

    void IncreasePlaybackSpeed()
    {
      int[] speeds = { 1, 2, 5, 10, 50, 100, 200, 300 };
      int currentIndex = System.Array.IndexOf(speeds, dp.Playback.Speed);
      if(currentIndex < speeds.Length - 1)
      {
        dp.Playback.Speed = speeds[currentIndex + 1];
        cfg.u.PlaybackSpeed = dp.Playback.Speed;
        UpdatePlaybackSpeedMenu();
      }
    }

    // **********************************************************************

    void DecreasePlaybackSpeed()
    {
      int[] speeds = { 1, 2, 5, 10, 50, 100, 200, 300 };
      int currentIndex = System.Array.IndexOf(speeds, dp.Playback.Speed);
      if(currentIndex > 0)
      {
        dp.Playback.Speed = speeds[currentIndex - 1];
        cfg.u.PlaybackSpeed = dp.Playback.Speed;
        UpdatePlaybackSpeedMenu();
      }
    }

    // **********************************************************************
  }
}
