﻿using System;
using System.Collections.Generic;
using System.Windows;
using WebSocketSharp;
using ANWI;
using System.Collections.ObjectModel;
using System.Threading;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Controls;
using System.Diagnostics;

namespace Client {
	/// <summary>
	/// Main application window
	/// Shows personnel jackets, full roster, and active operations
	/// </summary>
	public partial class MainWindow : MailboxWindow, INotifyPropertyChanged {

		public event PropertyChangedEventHandler PropertyChanged;

		private bool updated = false;

		private AuthenticatedAccount account = null;
		private Profile currentProfile = null;
		
		private ObservableCollection<LiteProfile> rosterList 
			= new ObservableCollection<LiteProfile>();

		private ObservableCollection<LiteOperation> operationList 
			= new ObservableCollection<LiteOperation>();

		#region Other Windows
		private FleetRegistry fleetReg = null;
		private OperationDetails opDet = null;
		#endregion

		#region Observable Members
		public Profile wpfProfile {
			get { return currentProfile; }
			set {
				if (currentProfile != value) {
					currentProfile = value;
					NotifyPropertyChanged("wpfProfile");
					NotifyPropertyChanged("canSetPrimaryRate");
					NotifyPropertyChanged("canEditName");
					NotifyPropertyChanged("canChangeRank");
				}
			}
		}
		public ObservableCollection<LiteProfile> wpfRosterList {
			get { return rosterList; }
		}
		public ObservableCollection<LiteOperation> wpfOpList {
			get { return operationList; }
		}

		public Privs userPrivileges { get { return account.profile.privs; } }
		public bool canSetPrimaryRate { get {
				return currentProfile != null && 
					account.profile.id == currentProfile.id;
			} }
		public bool canEditName {
			get { return currentProfile != null && 
					account.profile.id == currentProfile.id; }
		}
		public bool canChangeRank {
			get {
				return account.profile.privs.canPromote &&
					currentProfile != null &&
					currentProfile.rank.ordering < account.profile.rank.ordering;
			}
		}

		public bool connected {
			get {
				return MessageRouter.Instance.IsConnected(
					MessageRouter.Service.Main);
			} }
		public string connectionString { get {
				if(connected) {
					return "Connected";
				} else {
					return "Reconnect: 00:00";
				}
			} }
		public string connLightGreen { get { return connected ? "Circle" : "CircleOutline"; } }
		public string connLightRed { get { return connected ? "CircleOutline" : "Circle"; } }
		#endregion

		#region Initialization
		/// <summary>
		/// Form constructor
		/// Opens a modal login window first.  When control returns it checks 
		/// if there's a valid user it populates the rest of the window.
		/// </summary>
		public MainWindow() {
			
			// Last resort for any unhandled exceptions
			AppDomain.CurrentDomain.UnhandledException 
				+= new UnhandledExceptionEventHandler(UEHandler);

			AddProcessor(typeof(ANWI.Messaging.FullRoster), 
				ProcessRoster);
			AddProcessor(typeof(ANWI.Messaging.FullOperationsList), 
				ProcessOpsList);
			AddProcessor(typeof(ANWI.Messaging.AllCommonData), 
				ProcessCommonData);
			AddProcessor(typeof(ANWI.Messaging.ConfirmUpdate), 
				ProcessConfirmUpdate);
			AddProcessor(typeof(ANWI.Messaging.FullProfile), 
				ProcessFullProfile);
			AddProcessor(typeof(ANWI.Messaging.Ops.NewOpCreated),
				ProcessNewOpCreated);

			MessageRouter.Instance.onOpen += OnSocketOpen;
			MessageRouter.Instance.onClose += OnSocketClose;
			MessageRouter.Instance.onError += OnSocketError;

#if !DEBUG
			//
			// Open the splash screen which doubles as the updater
			Splash splash = new Splash();
			splash.UpdateDownloaded += OnUpdateDownloaded;
			splash.ShowDialog();
#endif

			if (updated) {
				Application.Current.Shutdown();
				return;
			}

			// Open a modal login window
			// When the window closes the authclient member will be either null
			// or have a structure.
			// If null, the login failed.
			Login logwin = new Client.Login();
			logwin.returnuser += value => account = value;
			logwin.ShowDialog();

			// Once the control returns check if we have a valid account
			if(account == null || account.profile == null) {
				// Just quit the app
				Application.Current.Shutdown();
				return;
			}

			MessageRouter.Instance.SetAccount(account);

			// Once we have the basic data initialize the window
			this.DataContext = this;
			InitializeComponent();

			wpfProfile = account.profile;

			// Open connection to the main service
			MessageRouter.Instance.Connect(MessageRouter.Service.Main);
			MessageRouter.Instance.Connect(MessageRouter.Service.Ops);

			// Note: blocking
			FetchCommonData();

			FetchRoster();
			FetchOps();

		}
#endregion

#region Window Event Handlers
		/// <summary>
		/// Closes the application
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void QuitButton_Click(object sender, RoutedEventArgs e) {
			Application.Current.Shutdown();
		}

		/// <summary>
		/// Refreshes the roster listbox
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_RefreshRoster_Click(object sender,
			RoutedEventArgs e) {
			FetchRoster();
		}

		/// <summary>
		/// Refreshes the operations listbox
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_RefreshOps_Click(object sender, 
			RoutedEventArgs e) {
			FetchOps();
		}

		/// <summary>
		/// Starts a new operation
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_NewOp_Click(object sender, RoutedEventArgs e) {
			if (opDet == null) {
				Button_NewOp.IsEnabled = false;

				Operations.NewOperation newOp = new Operations.NewOperation();
				newOp.returnNewOp += (name, type) => {
					MessageRouter.Instance.Send(
						MessageRouter.Service.Ops,
						new ANWI.Messaging.Ops.CreateNewOp(name, type,
						account.profile.id),
						this
						);
				};
				newOp.ShowDialog();
			}
		}

		/// <summary>
		/// Opens an operation's details window
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_OpenOp_Click(object sender, RoutedEventArgs e) {
			if(List_Ops.SelectedIndex >= 0) {
				if(opDet == null) {
					LiteOperation op = List_Ops.SelectedItem as LiteOperation;
					opDet = new OperationDetails(account.profile, op.uuid);
					opDet.OnClose += (a) => { opDet = null; };
					opDet.Show();
				}
			}
		}

		/// <summary>
		/// Opens the AddRate window
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_AddRate_Click(object sender, RoutedEventArgs e) {
			AddRate ar = new AddRate();
			ar.returnNewRate += (rateId, rank) => {
				AddRate(currentProfile.id, rateId, rank);
			};
			ar.ShowDialog();
		}

		/// <summary>
		/// Removes a rate from the currently selected user
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_DeleteRate_Click(object sender, RoutedEventArgs e) {
			Rate selected = (List_Rates.SelectedItem as Rate);
			if (selected != null) {
				Confirm c = new Confirm(
					$"Are you sure you want to delete {selected.fullName} from"+
					$" {currentProfile.rank.abbrev} {currentProfile.nickname}?"
					);
				c.yesAction += () => {
					DeleteRate(currentProfile.id, selected.struckId);
				};
				c.ShowDialog();
			}
		}

		/// <summary>
		/// Sets the primary rate for the currently selected user
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_SetPrimaryRate_Click(object sender, 
			RoutedEventArgs e) {
			Rate selected = (List_Rates.SelectedItem as Rate);
			if (selected != null) {  
				Confirm c = new Confirm(
					$"Are you sure you want to change your rate from " +
					$"{currentProfile.primaryRate.fullName} to " +
					$"{selected.fullName}?"
					);
				c.yesAction += () => {
					SetPrimaryRate(currentProfile.id, selected.struckId);
				};
				c.ShowDialog();
			}
		}

		/// <summary>
		/// Opens the ChangeRank window
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_ChangeRank_Click(object sender, RoutedEventArgs e) {
			ChangeRank cr = new ChangeRank(account.profile.rank.ordering);
			cr.ReturnNewRank += (rank) => {
				ChangeRank(currentProfile.id, rank);
			};
			cr.ShowDialog();
		}

		/// <summary>
		/// Requests the full profile of the selected user
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_ViewJacket_Click(object sender, RoutedEventArgs e) {
			if(List_Roster.SelectedItem != null) {
				LiteProfile p = List_Roster.SelectedItem as LiteProfile;
				MessageRouter.Instance.Send(
					MessageRouter.Service.Main,
					new ANWI.Messaging.Request(
						ANWI.Messaging.Request.Type.GetProfile, p.id),
					this
					);
			}
		}

		/// <summary>
		/// Opens the FleetRegistry window
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_OpenFleetReg_Click(object sender, 
			RoutedEventArgs e) {
			if (fleetReg == null) {
				fleetReg = new FleetRegistry(account.profile);
				fleetReg.OnClose += (t) => { fleetReg = null; };
				fleetReg.Show();
			} else {
				fleetReg.Activate();
			}
		}

		/// <summary>
		/// Edit user nickname button
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Button_EditName_Click(object sender, RoutedEventArgs e) {
			SimpleTextPrompt prompt = new SimpleTextPrompt("Edit Name", 
				currentProfile.nickname);
			prompt.ReturnText += (name) => {
				ChangeNickname(currentProfile.id, name);
			};
			prompt.ShowDialog();
		}

		private void Radio_Roster_Checked(object sender, RoutedEventArgs e) {
			CollectionViewSource sorted = null;

			if (Radio_Roster_Name.IsChecked.Value) {
				sorted = FindResource("SortedRosterName")
					as CollectionViewSource;
			} else {
				sorted = FindResource("SortedRosterRank")
					as CollectionViewSource;
			}

			List_Roster.SetBinding(ListBox.ItemsSourceProperty, new Binding() {
				Source = sorted
			});
		}

		/// <summary>
		/// Causes the application to quit when the main window closes
		/// </summary>
		/// <param name="e"></param>
		protected override void OnClosed(EventArgs e) {
			base.OnClosed(e);

			Application.Current.Shutdown();
		}
#endregion
		
#region Request Senders
		/// <summary>
		/// Gets commonly used data like the list of ranks and rates from 
		/// the server for storing globally.
		/// Waits in a loop until the response is received so the program can't 
		/// move forward without the data.
		/// </summary>
		private void FetchCommonData() {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.GetCommonData),
				this
				);

			while (!CommonData.loaded) {
				Thread.Sleep(10);
			}
		}

		/// <summary>
		/// Sends a request to the server for the full member roster
		/// </summary>
		private void FetchRoster() {
			// Clear the old list
			this.Dispatcher.Invoke(() => {
				rosterList.Clear();
				Spinner_Roster.Visibility = Visibility.Visible;
			});

			// Send a request to the server
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.GetRoster),
				this
				);
		}

		/// <summary>
		/// Sends a request to the server for the list of active operations
		/// </summary>
		private void FetchOps() {
			// Clear the old list
			this.Dispatcher.Invoke(() => {
				operationList.Clear();
				Spinner_Ops.Visibility = Visibility.Visible;
			});

			// Send a request to the server
			MessageRouter.Instance.Send(
				MessageRouter.Service.Ops,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.GetOperations),
				this
				);
		}

		/// <summary>
		/// Sends a request for a user's full profile
		/// </summary>
		/// <param name="userId"></param>
		private void FetchProfile(int userId) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.GetProfile, userId),
				this
				);
		}
#endregion

#region Response Processors

		/// <summary>
		/// Response handler for the full roster request.
		/// Populates the global roster list
		/// </summary>
		/// <param name="p"></param>
		private void ProcessRoster(ANWI.Messaging.IMessagePayload p) {
			ANWI.Messaging.FullRoster fr = p as ANWI.Messaging.FullRoster;

			this.Dispatcher.Invoke(() => {
				rosterList.Clear();
				Spinner_Roster.Visibility = Visibility.Hidden;
			});

			// Load all the records in
			foreach (LiteProfile pf in fr.members) {
				this.Dispatcher.Invoke(() => {
					if (pf.id == account.profile.id)
						pf.isMe = true;
					rosterList.Add(pf);
				});
			}
		}

		/// <summary>
		/// Loads the list of operations
		/// </summary>
		/// <param name="p"></param>
		private void ProcessOpsList(ANWI.Messaging.IMessagePayload p) {
			ANWI.Messaging.FullOperationsList fol
				= p as ANWI.Messaging.FullOperationsList;

			this.Dispatcher.Invoke(() => {
				operationList.Clear();
				Spinner_Ops.Visibility = Visibility.Hidden;
			});

			// Sort records by status
			fol.ops.Sort((a, b) => {
				return a.status.CompareTo(b.status);
			});

			// Insert each into the list
			foreach(LiteOperation op in fol.ops) {
				this.Dispatcher.Invoke(() => { operationList.Add(op); });
			}
		}

		/// <summary>
		/// Loads the common data into the static class
		/// </summary>
		/// <param name="p"></param>
		private void ProcessCommonData(ANWI.Messaging.IMessagePayload p) {
			CommonData.LoadAll(p as ANWI.Messaging.AllCommonData);
		}

		/// <summary>
		/// When an update confirmation is received for a profile, reload
		/// that profile to reflect the changes
		/// </summary>
		/// <param name="p"></param>
		private void ProcessConfirmUpdate(ANWI.Messaging.IMessagePayload p) {
			ANWI.Messaging.ConfirmUpdate cpu
					= p as ANWI.Messaging.ConfirmUpdate;
			FetchProfile(cpu.updatedId);
		}

		/// <summary>
		/// Load the received profile into the left pane
		/// </summary>
		/// <param name="p"></param>
		private void ProcessFullProfile(ANWI.Messaging.IMessagePayload p) {
			ANWI.Messaging.FullProfile fp = p as ANWI.Messaging.FullProfile;
			wpfProfile = fp.profile;
		}

		/// <summary>
		/// Opens the ops window with the new operation
		/// </summary>
		/// <param name="p"></param>
		private void ProcessNewOpCreated(ANWI.Messaging.IMessagePayload p) {
			ANWI.Messaging.Ops.NewOpCreated noc
				= p as ANWI.Messaging.Ops.NewOpCreated;

			this.Dispatcher.Invoke(() => {
				opDet = new OperationDetails(account.profile, noc.uuid);
				opDet.OnClose += (a) => { opDet = null; };
				opDet.Show();

				Button_NewOp.IsEnabled = true;
			});
		}
#endregion

#region Callbacks
		/// <summary>
		/// Adds a rate to the given user
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="rateId"></param>
		/// <param name="rank"></param>
		private void AddRate(int userId, int rateId, int rank) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.AddRate(userId, rateId, rank),
				this
				);
		}

		/// <summary>
		/// Removes a rate from the given user
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="rateId"></param>
		private void DeleteRate(int userId, int rateId) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.DeleteRate,
					new ANWI.Messaging.ReqExp.TwoIDs(userId, rateId)),
				this
				);
		}

		/// <summary>
		/// Sets the primary rate of a given user
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="rateId"></param>
		private void SetPrimaryRate(int userId, int rateId) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.SetPrimaryRate,
					new ANWI.Messaging.ReqExp.TwoIDs(userId, rateId)),
				this
				);
		}

		/// <summary>
		/// Changes the rank of a given user
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="rankId"></param>
		private void ChangeRank(int userId, int rankId) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.ChangeRank,
					new ANWI.Messaging.ReqExp.TwoIDs(userId, rankId)),
				this
				);
		}

		/// <summary>
		/// Changes the name of a given user
		/// </summary>
		/// <param name="userId"></param>
		/// <param name="name"></param>
		private void ChangeNickname(int userId, string name) {
			MessageRouter.Instance.Send(
				MessageRouter.Service.Main,
				new ANWI.Messaging.Request(
					ANWI.Messaging.Request.Type.ChangeName,
					new ANWI.Messaging.ReqExp.IdString(userId, name)),
				this
				);
		}
		#endregion

		private void OnSocketOpen(MessageRouter.Service svc) {
			NotifyPropertyChanged("connected");
			NotifyPropertyChanged("connectionString");
			NotifyPropertyChanged("connLightRed");
			NotifyPropertyChanged("connLightGreen");
		}

		private void OnSocketClose(MessageRouter.Service svc, CloseEventArgs c) {
			/*this.Dispatcher.Invoke(() => {
				MessageBox.Show(this,
					"Connection to Main service closed by server.  " +
					$"Reason: {c.Reason}", "Connection Closed",
					MessageBoxButton.OK,
					MessageBoxImage.Error);

				Application.Current.Shutdown();
			});*/

			NotifyPropertyChanged("connected");
			NotifyPropertyChanged("connectionString");
			NotifyPropertyChanged("connLightRed");
			NotifyPropertyChanged("connLightGreen");
		}

		private void OnSocketError(MessageRouter.Service svc, ErrorEventArgs e) {
			/*this.Dispatcher.Invoke(() => {
				MessageBox.Show("Socket Error.  Connection Lost", "Error",
					MessageBoxButton.OK);

				Application.Current.Shutdown();
			});*/

			NotifyPropertyChanged("connected");
			NotifyPropertyChanged("connectionString");
			NotifyPropertyChanged("connLightRed");
			NotifyPropertyChanged("connLightGreen");
		}

		private bool OnUpdateDownloaded(string archiveName) {
			int thisPID = Process.GetCurrentProcess().Id;

			string name = Process.GetCurrentProcess().ProcessName;

			Process updater = new Process();
			updater.StartInfo = new ProcessStartInfo(
				"patcher.exe",
				$"{name} {archiveName}");

			try {
				updated = true;
				updater.Start();
				return true;
			} catch(Exception e) {
				return false;
			}
		}

		/// <summary>
		/// Notifies the UI when a bound property changes
		/// </summary>
		/// <param name="name"></param>
		public void NotifyPropertyChanged(string name) {
			if(PropertyChanged != null) {
				PropertyChanged(this, new PropertyChangedEventArgs(name));
			}
		}

		/// <summary>
		/// Writes crash dumps in the event of a fatal exception
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private static void UEHandler(object sender, 
			UnhandledExceptionEventArgs e) {
			if (e.IsTerminating) {
				ANWI.Utility.DumpWriter.MiniDumpToFile("crashdump.dmp");
				Process.GetCurrentProcess().Kill();
			}
		}
	}
}
