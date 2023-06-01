﻿using StereoKit.Framework;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Collections.Concurrent;

namespace StereoKit
{
	/// <summary>This class contains functions for running the StereoKit 
	/// library!</summary>
	public static class SK
	{
		private static SystemInfo              _system;
		private static Steppers                _steppers         = new Steppers();
		private static ConcurrentQueue<Action> _mainThreadInvoke = new ConcurrentQueue<Action>();

		/// <summary>This is a copy of the settings that StereoKit was
		/// initialized with, so you can refer back to them a little easier.
		/// These are read only, and keep in mind that some settings are 
		/// only requests! Check SK.System and other properties for the 
		/// current state of StereoKit.</summary>
		public static SKSettings Settings { get; private set; }
		/// <summary>Has StereoKit been successfully initialized already? If
		/// initialization was attempted and failed, this value will be 
		/// false.</summary>
		public static bool IsInitialized { get; private set; }
		/// <summary>Since we can fallback to a different DisplayMode, this 
		/// lets you check to see which Runtime was successfully initialized.
		/// </summary>
		public static DisplayMode ActiveDisplayMode => NativeAPI.sk_active_display_mode();
		/// <summary>This structure contains information about the current 
		/// system and its capabilities. There's a lot of different MR devices,
		/// so it's nice to have code for systems with particular 
		/// characteristics!</summary>
		public static SystemInfo System => _system;

		/// <summary>Human-readable version name embedded in the StereoKitC
		/// DLL.</summary>
		public static string VersionName => Marshal.PtrToStringAnsi( NativeAPI.sk_version_name() );
		/// <summary>An integer version Id! This is defined using a hex value
		/// with this format: `0xMMMMiiiiPPPPrrrr` in order of 
		/// Major.mInor.Patch.pre-Release</summary>
		public static ulong VersionId => NativeAPI.sk_version_id();

		/// <summary> This tells about the app's current focus state,
		/// whether it's active and receiving input, or if it's
		/// backgrounded or hidden. This can be important since apps may
		/// still run and render when unfocused, as the app may still be
		/// visible behind the app that _does_ have focus. </summary>
		public static AppFocus AppFocus => NativeAPI.sk_app_focus();

		/// <summary>On Android systems, this must be assigned right away,
		/// before _any_ access to SK methods. When using the SK templates,
		/// this will be done automatically. This will be set to null after
		/// SK.Initialize is called.</summary>
		public static object AndroidActivity { get; set; }

		/// <summary>Initializes StereoKit window, default resources, systems,
		/// etc.</summary>
		/// <param name="settings">The configuration settings for StereoKit.
		/// This defines how StereoKit starts up and behaves, so it contains
		/// things like app name, assets folder, display mode, etc. The 
		/// default settings are meant to be good for most, but you may want
		/// to modify at least a few of these eventually!</param>
		/// <returns>Returns true if all systems are successfully 
		/// initialized!</returns>
		public static bool Initialize(SKSettings settings)
		{
			if (settings.androidActivity == IntPtr.Zero && AndroidActivity != null)
			{
				Type         javaObject = Type.GetType("Java.Lang.Object, Mono.Android");
				PropertyInfo handle     = javaObject?.GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public);
				if (handle != null)
					settings.androidActivity = (IntPtr)handle.GetValue(AndroidActivity);
			}
			if (AndroidActivity == null && settings.androidActivity != IntPtr.Zero)
			{
				Type activityType           = Type.GetType("Android.App.Activity, Mono.Android");
				Type jniHandleOwnershipType = Type.GetType("Android.Runtime.JniHandleOwnership, Mono.Android");
				ConstructorInfo constructor = activityType?.GetConstructor(new Type[] { typeof(IntPtr), jniHandleOwnershipType });
				if (constructor != null)
					AndroidActivity = constructor.Invoke(new object[] { settings.androidActivity, 0 }); ;
			}

			if (!NativeLib.Load()) {
				global::System.Diagnostics.Debug.WriteLine("[SK error] Failed to load StereoKitC!");
				Console.WriteLine                         ("[SK error] Failed to load StereoKitC!");
				return false;
			}

			IsInitialized = InitializeCall(settings);
			AndroidActivity = null;
			return IsInitialized;
		}

		/// <summary>This is a _very_ rudimentary way to initialize StereoKit,
		/// it doesn't take much, but a robust application will prefer to use
		/// an overload that takes SKSettings. This uses all the default
		/// settings, which are primarily configured for development.</summary>
		/// <param name="projectName">Name of the application, this shows up an
		/// the top of the Win32 window, and is submitted to OpenXR. OpenXR
		/// caps this at 128 characters.</param>
		/// <param name="assetsFolder">Where to look for assets when loading
		/// files! Final path will look like '[assetsFolder]/[file]', so a
		/// trailing '/' is unnecessary.</param>
		/// <returns>Returns true if all systems are successfully 
		/// initialized!</returns>
		public static bool Initialize(string projectName = null, string assetsFolder = "")
			=> Initialize(new SKSettings{ appName = projectName, assetsFolder = assetsFolder });

		/// <summary>If you need to call StereoKit code before calling
		/// SK.Initialize, you may need to explicitly load the library first.
		/// This can be useful for setting up a few things, but should
		/// probably be a pretty rare case.</summary>
		public static void PreLoadLibrary()
		{
			if (!NativeLib.Load())
			{
				global::System.Diagnostics.Debug.WriteLine("[SK error] Failed to load StereoKitC!");
				Console.WriteLine("[SK error] Failed to load StereoKitC!");
			}
		}

		/// <summary>Android only. This is for telling StereoKit about the
		/// active Android window surface. In particular, Xamarin's
		/// ISurfaceHolderCallback2 gets SurfaceCreated and SurfaceDestroyed
		/// events, and these events should feed into this function.</summary>
		/// <param name="window">This is an ISurfaceHolder.Surface.Handle or
		/// equivalent pointer.</param>
		public static void SetWindow(IntPtr window)
			=> NativeAPI.sk_set_window_xam(window);

		private static bool InitializeCall(SKSettings settings)
		{
			// Prep console for colored debug logs
			Log.SetupConsole();

			// Make sure we have a valid name
			if (string.IsNullOrEmpty(settings.appName))
			{
				try { settings.appName = Assembly.GetEntryAssembly()?.GetName()?.Name ?? "StereoKit App"; }
				catch { settings.appName = "StereoKit App"; }
			}

			// If we don't have an android activity, the loader will also have
			// one we can use.
			if (settings.androidActivity == IntPtr.Zero)
				settings.androidActivity = NativeLib.androidActivityHandle;

			// DllImport finds the function at the beginning of the function 
			// call, so this needs to be in a separate function from 
			// NativeLib.LoadDll
			bool result = NativeAPI.sk_init(settings);
			Settings = settings;

			// Get system information
			if (result) { 
				_system = NativeAPI.sk_system_info();
				Default.Initialize();
			}

			Backend.OpenXR.CleanupInitialize();

			return result;
		}

		/// <summary>Cleans up all StereoKit initialized systems. Release your
		/// own StereoKit created assets before calling this. This is for
		/// cleanup only, and should not be used to exit the application, use
		/// SK.Quit for that instead. Calling this function is unnecessary if
		/// using SK.Run, as it is called automatically there.</summary>
		public static void Shutdown()
		{
			if (NativeAPI.sk_is_stepping())
				throw new Exception("SK.Shutdown is for cleanup and should not be used within SK.Step/Run, please use SK.Quit to exit your app!");

			if (IsInitialized)
			{
				_steppers.Shutdown();
				Default.Shutdown();
				NativeAPI.sk_shutdown();
			}
		}

		/// <summary>Lets StereoKit know it should quit! It'll finish the
		/// current frame, and after that Step will return that it wants to
		/// exit.</summary>
		public static void Quit()
		{
			NativeAPI.sk_quit();
		}

		/// <summary> Steps all StereoKit systems, and inserts user code via
		/// callback between the appropriate system updates. </summary>
		/// <param name="onStep">A callback where you put your application
		/// code! This gets called between StereoKit systems, after frame
		/// setup, but before render.</param>
		/// <returns>If an exit message is received from the platform, or
		/// `SK.Quit()` is called, this function will return false.</returns>
		public static bool Step(Action onStep = null)
		{
			_stepCallback = onStep;
			return NativeAPI.sk_step(_stepAction);
		}
		// This pattern is a little weird, but it avoids continuous Action
		// allocations, and saves our GC a surprising amount of work.
		private static Action _stepAction   = _Step;
		private static Action _stepCallback = null;
		private static void _Step() {
			_steppers.Step();
			_stepCallback?.Invoke();

			while (_mainThreadInvoke.TryDequeue(out Action a))
				a.Invoke();
		}

		private static Action _shutdownCallback = null;
		/// <summary>This passes application execution over to StereoKit. 
		/// This continuously steps all StereoKit systems, and inserts user
		/// code via callback between the appropriate system updates. Once
		/// execution completes, or `SK.Quit` is called, it properly calls the
		/// shutdown callback and shuts down StereoKit for you.
		/// 
		/// Using this method is important for compatibility with WASM and is
		/// the preferred method of controlling the main loop, over 
		/// `SK.Step`.</summary>
		/// <param name="onStep">A callback where you put your application 
		/// code! This gets called between StereoKit systems, after frame 
		/// setup, but before render.</param>
		/// <param name="onShutdown">A callback that gives you the
		/// opportunity to shut things down while StereoKit is still active.
		/// This is called after the last Step completes, and before
		/// StereoKit shuts down.</param>
		public static void Run(Action onStep = null, Action onShutdown = null)
		{
			_stepCallback = onStep;
			_shutdownCallback = onShutdown;
			NativeAPI.sk_run(_stepAction, _shutdownCallback);
		}

		/// <summary>This registers an instance of the `IStepper` type
		/// provided. SK will hold onto it, Initialize it, Step it every frame,
		/// and call Shutdown when the application ends. This is generally safe
		/// to do before SK.Initialize is called, the constructor is called
		/// right away, and Initialize is called right after SK.Initialize, or
		/// at the start of the next frame before the next main Step callback
		/// if SK is already initialized.</summary>
		/// <param name="stepper">An instance of an IStepper object.</param>
		/// <typeparam name="T">An IStepper type.</typeparam>
		/// <returns>Just for convenience, this returns the instance that was
		/// just added.</returns>
		public static T AddStepper<T>(T stepper) where T:IStepper => _steppers.Add(stepper);
		/// <summary>This instantiates and registers an instance of the
		/// `IStepper` type provided as the generic parameter. SK will hold
		/// onto it, Initialize it, Step it every frame, and call Shutdown when
		/// the application ends. This is generally safe to do before
		/// SK.Initialize is called, the constructor is called right away, and
		/// Initialize is called right after SK.Initialize, or at the start of
		/// the next frame before the next main Step callback if SK is already
		/// initialized.</summary>
		/// <param name="type">Any object that implements IStepper, and has a
		/// constructor with zero parameters.</param>
		/// <returns>Just for convenience, this returns the instance that was
		/// just added.</returns>
		public static object AddStepper(Type type) => _steppers.Add(type);
		/// <summary>This instantiates and registers an instance of the
		/// `IStepper` type provided as the generic parameter. SK will hold
		/// onto it, Initialize it, Step it every frame, and call Shutdown when
		/// the application ends. This is generally safe to do before
		/// SK.Initialize is called, the constructor is called right away, and
		/// Initialize is called right after SK.Initialize, or at the start of
		/// the next frame before the next main Step callback if SK is already
		/// initialized.</summary>
		/// <typeparam name="T">An IStepper type.</typeparam>
		/// <returns>Just for convenience, this returns the instance that was
		/// just added.</returns>
		public static T AddStepper<T>() where T:IStepper => _steppers.Add<T>();
		/// <summary>This removes a specific IStepper from SK's IStepper list.
		/// This will call the IStepper's Shutdown method before returning.
		/// </summary>
		/// <param name="stepper">The specific IStepper instance to remove.
		/// </param>
		public static void RemoveStepper(IStepper stepper) => _steppers.Remove(stepper);
		/// <summary>This removes all IStepper instances that are assignable to
		/// the generic type specified. This will call the IStepper's Shutdown
		/// method on each removed instance before returning.</summary>
		/// <param name="type">Any parent or exact type.</param>
		public static void RemoveStepper(Type type) => _steppers.Remove(type);
		/// <summary>This removes all IStepper instances that are assignable to
		/// the generic type specified. This will call the IStepper's Shutdown
		/// method on each removed instance before returning.</summary>
		/// <typeparam name="T">Any parent or exact type.</typeparam>
		public static void RemoveStepper<T>() => _steppers.Remove<T>();

		/// <summary>This will search the list of `IStepper`s that are
		/// currently attached to StereoKit. This includes `IStepper`s that
		/// have been added but are not yet initialized. This will return the
		/// first `IStepper` in the list that is assignable to the provided
		/// generic type.</summary>
		/// <typeparam name="T">Any parent or exact type.</typeparam>
		/// <returns>The first `IStepper` in the list that is assignable to the
		/// provided generic type, or null if none is found.</returns>
		public static T GetStepper<T>() => _steppers.Get<T>();
		/// <summary>This will search the list of `IStepper`s that are
		/// currently attached to StereoKit. This includes `IStepper`s that
		/// have been added but are not yet initialized. This will return the
		/// first `IStepper` in the list that is assignable to the provided
		/// type.</summary>
		/// <param name="type">Any parent or exact type.</param>
		/// <returns>The first `IStepper` in the list that is assignable to the
		/// provided generic type, or null if none is found.</returns>
		public static object GetStepper(Type type) => _steppers.Get(type);
		/// <summary>This will search the list of `IStepper`s that are
		/// currently attached to StereoKit. This includes `IStepper`s that
		/// have been added but are not yet initialized. This will return the
		/// first `IStepper` in the list that is assignable to the provided
		/// generic type, and if one is not found, it will attempt to create
		/// one.</summary>
		/// <typeparam name="T">Any concrete type that contains an empty
		/// constructor.</typeparam>
		/// <returns>The first `IStepper` in the list that is assignable to the
		/// provided generic type, or a new object of type T.</returns>
		public static T GetOrCreateStepper<T>() where T : IStepper
		{
			T result = _steppers.Get<T>();
			if (result == null) result = _steppers.Add<T>();
			return result;
		}
		/// <summary>This will search the list of `IStepper`s that are
		/// currently attached to StereoKit. This includes `IStepper`s that
		/// have been added but are not yet initialized. This will return the
		/// first `IStepper` in the list that is assignable to the provided
		/// type, and if one is not found, it will attempt to create one.
		/// </summary>
		/// <param name="type">Any concrete type that contains an empty
		/// constructor.</param>
		/// <returns>The first `IStepper` in the list that is assignable to the
		/// provided generic type, or a new object of the given type.</returns>
		public static object GetOrCreateStepper(Type type)
		{
			object result = _steppers.Get(type);
			if (result == null) result = _steppers.Add(type);
			return result;
		}

		/// <summary>This will queue up some code to be run on StereoKit's main
		/// thread! Immediately after StereoKit's Step, all callbacks
		/// registered here will execute, and then removed from the list.
		/// </summary>
		/// <param name="action">Some code to run! This Action will persist in
		/// a list until after Step, at which point it is removed and dropped.
		/// </param>
		public static void ExecuteOnMain(Action action) => _mainThreadInvoke.Enqueue(action);
	}
}
