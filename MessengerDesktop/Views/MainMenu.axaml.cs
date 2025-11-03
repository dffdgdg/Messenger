using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using MessengerDesktop.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Views
{
    public partial class MainMenuView : UserControl
    {
        public MainMenuView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContextChanged += MainMenuView_DataContextChanged;
        }

        private void MainMenuView_DataContextChanged(object? sender, EventArgs e)
        {
            if (sender is not null && DataContext is MainMenuViewModel vm)
            {
                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private async void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (sender is MainMenuViewModel vm)
                {
                    if (e.PropertyName == nameof(vm.UserProfileDialog))
                    {
                        if (vm.UserProfileDialog != null)
                        {
                            await Task.Delay(10);
                            await PlayShowAnimationAsync("ProfileDialogOverlay");
                        }
                    }
                    else if (e.PropertyName == nameof(vm.IsPollDialogOpen))
                    {
                        if (vm.IsPollDialogOpen)
                        {
                            await Task.Delay(10);
                            await PlayShowAnimationAsync("PollDialogOverlay");
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private async Task PlayShowAnimationAsync(string overlayName)
        {
            try
            {
                var overlay = this.FindControl<Grid>(overlayName);
                if (overlay == null)
                {
                    return;
                }

                Control? dialog = null;
                if (overlayName == "ProfileDialogOverlay")
                {
                    dialog = this.FindControl<Control>("UserProfileDialog");
                }
                else if (overlayName == "PollDialogOverlay")
                {
                    dialog = this.FindControl<Control>("PollDialog");
                }

                ScaleTransform? scale = null;
                TranslateTransform? translate = null;

                if (dialog != null)
                {
                    if (dialog.RenderTransform is TransformGroup tg)
                    {
                        foreach (var t in tg.Children)
                        {
                            if (t is ScaleTransform s) scale = s;
                            if (t is TranslateTransform tr) translate = tr;
                        }
                    }
                    else
                    {
                        var tgNew = new TransformGroup();
                        scale = new ScaleTransform { ScaleX = 1d, ScaleY = 1d };
                        translate = new TranslateTransform { X = 0d, Y = 0d };
                        tgNew.Children.Add(scale);
                        tgNew.Children.Add(translate);
                        dialog.RenderTransform = tgNew;
                    }

                    if (dialog is Control ctrl)
                        ctrl.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                }

                var overlayAnim = new Animation
                {
                    Duration = TimeSpan.FromSeconds(0.12),
                    FillMode = FillMode.Forward,
                    Easing = new CubicEaseOut()
                };

                overlayAnim.Children.Add(new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Grid.OpacityProperty, 0d) }
                });

                overlayAnim.Children.Add(new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Grid.OpacityProperty, 1d) }
                });

                Task t1 = overlayAnim.RunAsync(overlay, CancellationToken.None);

                if (dialog != null && scale != null && translate != null)
                {
                    var dialogAnim = new Animation
                    {
                        Duration = TimeSpan.FromSeconds(0.18),
                        Easing = new CubicEaseOut(),
                        FillMode = FillMode.Forward
                    };

                    dialogAnim.Children.Add(new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters = {
                            new Setter(Control.OpacityProperty, 0d),
                            new Setter(ScaleTransform.ScaleXProperty, 0.7d),
                            new Setter(ScaleTransform.ScaleYProperty, 0.7d),
                            new Setter(TranslateTransform.YProperty, 18d)
                        }
                    });

                    dialogAnim.Children.Add(new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = {
                            new Setter(Control.OpacityProperty, 1d),
                            new Setter(ScaleTransform.ScaleXProperty, 1d),
                            new Setter(ScaleTransform.ScaleYProperty, 1d),
                            new Setter(TranslateTransform.YProperty, 0d)
                        }
                    });

                    var t2 = dialogAnim.RunAsync(dialog, CancellationToken.None);
                    await Task.WhenAll(t1, t2);
                }
                else
                    await t1;
            }
            catch (Exception)
            {
            }
        }

        private void OnProfileBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainMenuViewModel vm && vm.UserProfileDialog != null)
            {
                vm.UserProfileDialog.CloseAction?.Invoke();
                e.Handled = true;
            }
        }

        private void OnPollBackgroundPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is MainMenuViewModel vm && vm.PollDialogViewModel?.CloseAction != null)
            {
                vm.PollDialogViewModel.CloseAction.Invoke();
                e.Handled = true;
            }
        }
    }
}