////1. Define key combinations
//using GlobalHotKeys;
//using Gma.System.MouseKeyHook;
//using System.Windows.Forms;
//using Microsoft.Win32;
////var hkm = new HotKeyManager();
////var undo = Combination.FromString("Control+Z");
////var fullScreen = Combination.FromString("Shift+Alt+Enter");

////2. Define actions
//Action actionUndo = DoSomething;
//Action actionFullScreen = () => { Console.WriteLine("You Pressed FULL SCREEN"); };

//void DoSomething()
//{
//    Console.WriteLine("You pressed UNDO");
//}

////3. Assign actions to key combinations
////var assignment = new Dictionary<Combination, Action>
////{
////    {undo, actionUndo},
////    {fullScreen, actionFullScreen}
////};

////4. Install listener
//Hook.GlobalEvents().KeyDown += (s, e) =>
//{
//    Console.WriteLine($"DOWN: {e.Modifiers}  {e.Control} {e.KeyValue}");
//};

//Hook.GlobalEvents().KeyUp += (s, e) =>
//{
//    Console.WriteLine($"UP: {e.Modifiers}  {e.Control} {e.KeyValue}");
//};

//Hook.GlobalEvents().KeyPress += (s, e) =>
//{
//    Console.WriteLine($"Press: {e.KeyChar}  {e.Handled} ");
//};
////Hook.GlobalEvents().OnCombination(assignment);

//SystemEvents.PowerModeChanged += (s, e) =>
//{
//    Console.WriteLine(e.Mode);
//    switch (e.Mode)
//    {
//        case PowerModes.Suspend:
//            Console.WriteLine("SLEEPING");
//            break;
//        case PowerModes.Resume:
//            // After resume
//            Console.WriteLine("RESUMING");
//            break;
//    }
//};
//Application.Run();