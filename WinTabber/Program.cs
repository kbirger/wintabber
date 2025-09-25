//1. Define key combinations
using Gma.System.MouseKeyHook;

var undo = Combination.FromString("Control+Z");
var fullScreen = Combination.FromString("Shift+Alt+Enter");

//2. Define actions
Action actionUndo = DoSomething;
Action actionFullScreen = () => { Console.WriteLine("You Pressed FULL SCREEN"); };

void DoSomething()
{
    Console.WriteLine("You pressed UNDO");
}

//3. Assign actions to key combinations
var assignment = new Dictionary<Combination, Action>
{
    {undo, actionUndo},
    {fullScreen, actionFullScreen}
};

//4. Install listener
Hook.GlobalEvents().OnCombination(assignment);

