using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace binanceTrader
{
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    class Hook
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);
        // 내 프로그램에서 후킹을 시작할 때 사용


        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hInstance);
        // 내 프로그램에서 후킹을 해제할 때 사용


        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr idHook, int nCode, int wParam, IntPtr lParam);
        // 키 입력이 일어난 원 프로그램에 값을 전달


        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string lpFileName);
        // user32 호출하기 위해 사용


        [DllImport("user32.dll")]
        public static extern ushort GetAsyncKeyState(Int32 vKey);
        // 같이 눌린 키를 확인할 때 사용. 복합키 (CTRL + A 같은) 일때 사용.
        // C# Keyboard.GetKeyStates 로 대체 가능합니다.


        /**
         *  0x0000 이전에 누른 적이 없고 호출 시점에도 눌려있지 않은 상태
         *  0x0001 이전에 누른 적이 있고 호출 시점에는 눌려있지 않은 상태
         *  0x8000 이전에 누른 적이 없고 호출 시점에는 눌려있는 상태
         *  0x8001 이전에 누른 적이 있고 호출 시점에도 눌려있는 상태
        */
        enum RETURN_GetAsyncKeyState : ushort
        {
            NN = 0x0000, // 0
            YN = 0x0001, // 1
            NY = 0x8000, // 32768
            YY = 0x8001  // 32769
        }


        private static bool IsKeyPress(Keys key)
        {
            RETURN_GetAsyncKeyState rtnVal = (RETURN_GetAsyncKeyState)GetAsyncKeyState((int)key);
            return RETURN_GetAsyncKeyState.YN.Equals(rtnVal) || RETURN_GetAsyncKeyState.YY.Equals(rtnVal);
        }


        // 전역변수
        private static LowLevelKeyboardProc _proc = HookProc;
        private static IntPtr hhook = IntPtr.Zero;
        private static Dictionary<int, double> keys = new Dictionary<int, double>();

        private static OnKeyDown EventKeyDown;
        private static OnKeyUp EventKeyUp;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100;
        private const int WM_KEYUP = 0x101;
        private const int WM_SYSKEYDOWN = 0x104;
        private const int WM_SYSKEYUP = 0x105;



        public static IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            // 후킹할 내용만 정의
            if (code <= 0 && wParam != (IntPtr)WM_KEYDOWN && wParam != (IntPtr)WM_KEYUP
                && wParam != (IntPtr)WM_SYSKEYDOWN && wParam != (IntPtr)WM_SYSKEYUP)
            {
                return CallNextHookEx(hhook, code, (int)wParam, lParam);
            }

            int vkCode = Marshal.ReadInt32(lParam);
            Keys curKey = (Keys)vkCode;

            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                if (!keys.ContainsKey(vkCode))
                {
                    keys.Add(vkCode, GetTime());
                    EventKeyDown(vkCode);
                    //Trace.WriteLine(String.Format("KEY_DN : {0}", curKey.ToString()));
                }
            }
            else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
                double duration = 0;
                if (keys.TryGetValue(vkCode, out duration)) duration = GetTime() - duration;

                keys.Remove(vkCode);

                EventKeyUp(vkCode, duration);
                //Trace.WriteLine(String.Format("KEY_UP : {0} {1}ms", curKey.ToString(), duration));
            }


            return CallNextHookEx(hhook, code, (int)wParam, lParam);
        }


        public static double GetTime()
        {
            return (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
        }


        public static void SetHook(OnKeyDown handleKeyDown, OnKeyUp handleKeyUp)
        {
            EventKeyDown = handleKeyDown;
            EventKeyUp = handleKeyUp;

            IntPtr hInstance = LoadLibrary("User32");
            hhook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hInstance, 0);
        }


        public static void UnHook()
        {
            UnhookWindowsHookEx(hhook);
        }
    }
}
