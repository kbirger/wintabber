using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabber.Interop;

namespace WinTabberUI.ViewModels
{
    public class MediaControlsViewModel
    {

        public ReactiveCommand<Unit, Unit> PlayPause { get; init; }
        public ReactiveCommand<Unit, Unit> Next { get; init; }
        public ReactiveCommand<Unit, Unit> Prev { get; init; }

        public MediaControlsViewModel()
        {
            var scheduler = RxApp.MainThreadScheduler;
            PlayPause = ReactiveCommand.CreateFromObservable(
                PlayPauseImpl, 
                canExecute: null, 
                outputScheduler: scheduler);
            Next = ReactiveCommand.CreateFromObservable(
                NextImpl,
                canExecute: null,
                outputScheduler: scheduler);
            Prev = ReactiveCommand.CreateFromObservable(
                PrevImpl,
                canExecute: null,
                outputScheduler: scheduler);

            Observable.Merge(
                PlayPause.ThrownExceptions,
                Next.ThrownExceptions,
                Prev.ThrownExceptions
            ).Subscribe(ex =>
            {
                Debug.WriteLine("Error processing media keys");
                Debug.WriteLine(ex);
            });
        }

        private IObservable<Unit> PlayPauseImpl()
        {
            return Observable.Start(MediaKeySender.PlayPause);
        }

        private IObservable<Unit> PrevImpl()
        {
            return Observable.Start(MediaKeySender.Prev);

        }

        private IObservable<Unit> NextImpl()
        {
            return Observable.Start(MediaKeySender.Next);
        }

    }
}
