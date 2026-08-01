using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WinTabber.API;
using WinTabber.API.Suspension;

namespace WinTabberUI.ViewModels;

public class WindowItem : ReactiveObject, IDisposable
{
    public WindowItem(WindowRef windowRef, IObservable<bool> canEdit, IProcessSuspensionService suspensionService)
    {
        WindowRef = windowRef ?? throw new ArgumentNullException(nameof(windowRef));
        //Icon = WindowRef.GetIcon().ToImageSource();
        //EditTitleCommand = new EditTitleCommand(this);
        var editingChanges = this.WhenAnyValue(item => item.IsEditing);
        Title = WindowRef.Title;

        var isTitleValid = this.WhenAnyValue(item => item.Title).Select(IsTitleValid).StartWith(IsTitleValid(Title));
        SaveTitleCommand = ReactiveCommand.Create<string, string>(
            SetTitle,
            Observable
                .CombineLatest(isTitleValid, editingChanges, (a, b) => a && b)
                .DistinctUntilChanged());

        CancelEditTitleCommand = ReactiveCommand.Create(CancelEdit, editingChanges);
        StartEditCommand = ReactiveCommand.Create(() => StartEdit(), canEdit);
        _canEdit = canEdit.ToProperty(this, x => x.CanEdit);

        var pid = windowRef.Process.ProcessInstance.Id;

        var canSuspendChanges = suspensionService.Connect()
            .Select(_ => suspensionService.CanSuspend(WindowRef))
            .StartWith(suspensionService.CanSuspend(WindowRef));

        var canSuspend = Observable
            .CombineLatest(editingChanges, canSuspendChanges, (isEditing, canSuspend) => !isEditing && canSuspend)
            .DistinctUntilChanged();

        SuspendCommand = ReactiveCommand.Create(() => { suspensionService.Suspend(WindowRef); }, canSuspend);

        _isSuspended = suspensionService.Connect()
            .Select(_ => suspensionService.IsSuspended(pid))
            .StartWith(suspensionService.IsSuspended(pid))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.IsSuspended);

        //var editWatch = canEdit.Subscribe(value =>
        //{
        //    if (!value)
        //    {
        //        CancelEdit();
        //    }
        //});
        _cleanUp = new CompositeDisposable(
            SaveTitleCommand,
            CancelEditTitleCommand,
            StartEditCommand,
            SuspendCommand,
            _canEdit,
            _isSuspended);
    }

    public WindowRef WindowRef { get; }

    public ReactiveCommand<string, string> SaveTitleCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelEditTitleCommand { get; }
    public ReactiveCommand<Unit, Unit> StartEditCommand { get; }
    public ReactiveCommand<Unit, Unit> SuspendCommand { get; }

    public bool CanEdit => _canEdit.Value;

    public bool IsSuspended => _isSuspended.Value;

    private readonly ObservableAsPropertyHelper<bool> _canEdit;
    private readonly ObservableAsPropertyHelper<bool> _isSuspended;
    private readonly CompositeDisposable _cleanUp;

    public string ProcessName => WindowRef.Process.ProcessInstance.ProcessName;

    private static bool IsTitleValid(string title)
    {
        return !string.IsNullOrWhiteSpace(title) && 1 <= title.Length && title.Length <= 255;
    }

    private string SetTitle(string value)
    {
        WindowRef.SetTitle(value);
        IsEditing = false;
        return value;
    }

    private void CancelEdit()
    {
        Title = WindowRef.Title;
        IsEditing = false;
    }

    private void StartEdit()
    {
        IsEditing = true;
    }

    public string Title
    {
        get
        {
            return _title;
        }
        set
        {
           this.RaiseAndSetIfChanged(ref _title, value);
        }
    }

    private string _title;
  
    public nint Handle => WindowRef.Handle;

    private bool _isEditing = false;
    public bool IsEditing
    {
        get => _isEditing;
        private set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }


    //public ImageSource Icon { get; set; }

    public void Activate() => WindowRef.BringToFront();

    public void Dispose()
    {
        _cleanUp.Dispose();
    }
}
