#if BCL45
//
// Copyright 2014-2015 Amazon.com, 
// Inc. or its affiliates. All Rights Reserved.
// 
// SPDX-License-Identifier: Apache-2.0
//
using Amazon.CognitoIdentity;
using Amazon.CognitoSync.SyncManager.Internal;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Util;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;


namespace Amazon.CognitoSync.SyncManager
{

    public partial class Dataset : IDisposable
    {

        private void DatasetSetupInternal()
        {
            NetworkChange.NetworkAvailabilityChanged += HandleNetworkChange;
        }

        #region Dispose Methods
        /// <summary>
        /// Releases the resources consumed by this object if disposing is true. 
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                ClearAllDelegates();
                NetworkChange.NetworkAvailabilityChanged -= HandleNetworkChange;
                _disposed = true;
            }
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Attempt to synchronize <see cref="Dataset"/> when connectivity is available. If
        /// the connectivity is available right away, it behaves the same as
        /// <see cref="Dataset.SynchronizeAsync"/>. Otherwise it listens to connectivity
        /// changes, and will do a sync once the connectivity is back. Note that if
        /// this method is called multiple times, only the last synchronize request
        /// is kept. If either the dataset or the callback is garbage collected
        /// , this method will not perform a sync and the callback won't fire.
        /// </summary>
        public async Task SynchronizeOnConnectivity(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                await SynchronizeHelperAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                waitingForConnectivity = true;
            }
        }

        #endregion

        #region Private Methods

        private async void HandleNetworkChange(object sender, NetworkAvailabilityEventArgs e)
        {
            if (!waitingForConnectivity)
            {
                return;
            }

            if (e.IsAvailable)
            {
                await SynchronizeAsync().ConfigureAwait(false);
            }
        }

        #endregion

        /// <summary>
        /// Synchronize <see cref="Dataset"/> between local storage and remote storage.
        /// </summary>
        /// <seealso href="http://docs.aws.amazon.com/cognito/latest/developerguide/synchronizing-data.html#synchronizing-local-data">Amazon Cognito Sync Dev. Guide - Synchronizing Local Data with the Sync Store</seealso>
        public async Task SynchronizeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                FireSyncFailureEvent(new NetworkException("Network connectivity unavailable."));
                return;
            }

            await SynchronizeHelperAsync(cancellationToken).ConfigureAwait(false);
        }

        internal async Task SynchronizeHelperAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (locked)
                {
                    _logger.InfoFormat("Already in a Synchronize. Queueing new request.", DatasetName);
                    queuedSync = true;
                    return;
                }
                else
                {
                    locked = true;
                }

                waitingForConnectivity = false;

                //make a call to fetch the identity id before the synchronization starts
                await CognitoCredentials.GetIdentityIdAsync().ConfigureAwait(false);

                // there could be potential merges that could have happened due to reparenting from the previous step,
                // check and call onDatasetMerged
                bool resume = true;
                List<string> mergedDatasets = LocalMergedDatasets;
                if (mergedDatasets.Count > 0)
                {
                    _logger.InfoFormat("Detected merge datasets - {0}", DatasetName);

                    if (this.OnDatasetMerged != null)
                    {
                        resume = this.OnDatasetMerged(this, mergedDatasets);
                    }
                }

                if (!resume)
                {
                    FireSyncFailureEvent(new OperationCanceledException(string.Format("Sync canceled on merge for dataset - {0}", this.DatasetName)));
                    return;
                }
                await RunSyncOperationAsync(MAX_RETRY, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                FireSyncFailureEvent(e);
                _logger.Error(e, "");
            }
        }
    }
}
#endif