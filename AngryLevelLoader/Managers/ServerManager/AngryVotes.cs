﻿using AngryLevelLoader.DataTypes;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace AngryLevelLoader.Managers.ServerManager
{
	public static class AngryVotes
	{
		#region Get All Votes
		public enum GetAllVotesStatus
		{
			NETWORK_ERROR = -2,
			RATE_LIMITED = -1,
			GET_ALL_VOTES_OK = 0
		}

		public class GetAllVotesBundleInfo
		{
			public int upvotes { get; set; }
			public int downvotes { get; set; }
		}

		public class GetAllVotesResponse
		{
			public string message { get; set; }
			public int status { get; set; }
			public Dictionary<string, GetAllVotesBundleInfo> bundles;
		}

		public class GetAllVotesResult
		{
			public bool networkError = false;
			public bool httpError = false;

			public GetAllVotesStatus status = GetAllVotesStatus.NETWORK_ERROR;
			public GetAllVotesResponse response;
		}

		public static async Task<GetAllVotesResult> GetAllVotesTask(CancellationToken cancellationToken = default(CancellationToken))
		{
			GetAllVotesResult result = new GetAllVotesResult();

			UnityWebRequest req = new UnityWebRequest(AngryPaths.SERVER_ROOT + $"/votes");
			req.downloadHandler = new DownloadHandlerBuffer();
			cancellationToken.Register(() =>
			{
				if (!req.isDone)
					req.Abort();
			});
			await req.SendWebRequest();

			if (cancellationToken.IsCancellationRequested)
			{
				return result;
			}

			if (req.isNetworkError)
			{
				result.networkError = true;
				return result;
			}
			if (req.isHttpError)
			{
				result.httpError = true;
				return result;
			}

			GetAllVotesResponse response = JsonConvert.DeserializeObject<GetAllVotesResponse>(req.downloadHandler.text);
			result.status = (GetAllVotesStatus)response.status;
			result.response = response;

			return result;
		}
		#endregion

		#region Vote
		public const string VOTE_OP_UPVOTE = "upvote";
		public const string VOTE_OP_DOWNVOTE = "downvote";
		public const string VOTE_OP_CLEAR = "clear";

		public enum VoteOperation
		{
			UPVOTE,
			DOWNVOTE,
			CLEAR,
			UNKNOWN
		}

		public enum VoteStatus
		{
			NETWORK_ERROR = -2,
			RATE_LIMITED = -1,
			VOTE_OK = 0,
			VOTE_INVALID_TOKEN = 1,
			VOTE_INVALID_BUNDLE = 2,
			VOTE_INVALID_OPERATION = 3,
		}

		public class VoteResponse
		{
			public string message { get; set; }
			public int status { get; set; }

			public string bundleGuid { get; set; }
			public string operation { get; set; }
			public int upvotes { get; set; }
			public int downvotes { get; set; }
		}

		public class VoteResult
		{
			public bool networkError = false;
			public bool httpError = false;

			public VoteStatus status = VoteStatus.NETWORK_ERROR;
			public VoteOperation operation;
			public VoteResponse response;
		}

		public static async Task<VoteResult> VoteTask(string bundleGuid, VoteOperation operation, bool tokenRequested = false)
		{
			VoteResult result = new VoteResult();

			bool invalidToken = string.IsNullOrEmpty(AngryUser.token);
			if (!invalidToken)
			{
				string op = VOTE_OP_CLEAR;
				if (operation == VoteOperation.UPVOTE)
					op = VOTE_OP_UPVOTE;
				else if (operation == VoteOperation.DOWNVOTE)
					op = VOTE_OP_DOWNVOTE;

				UnityWebRequest req = new UnityWebRequest(AngryPaths.SERVER_ROOT + $"/user/vote?steamId={AngryUser.steamId}&token={AngryUser.token}&bundleGuid={bundleGuid}&op={op}");
				req.downloadHandler = new DownloadHandlerBuffer();
				await req.SendWebRequest();

				if (req.isNetworkError)
				{
					result.networkError = true;
					return result;
				}
				if (req.isHttpError)
				{
					result.httpError = true;
					return result;
				}

				VoteResponse response = JsonConvert.DeserializeObject<VoteResponse>(req.downloadHandler.text);
				result.response = response;
				result.status = (VoteStatus)response.status;
				
				if (response.status == (int)VoteStatus.VOTE_INVALID_TOKEN)
				{
					invalidToken = true;
				}
				else
				{
					if (response.status == (int)VoteStatus.VOTE_OK)
					{
						result.operation = VoteOperation.CLEAR;
						if (response.operation == VOTE_OP_UPVOTE)
							result.operation = VoteOperation.UPVOTE;
						else if (response.operation == VOTE_OP_DOWNVOTE)
							result.operation = VoteOperation.DOWNVOTE;
					}

					return result;
				}
			}

			if (invalidToken)
			{
				result.status = VoteStatus.VOTE_INVALID_TOKEN;
				if (tokenRequested)
					return result;

                AngryUser.TokenGenResult tokenRes = await AngryUser.GenerateToken();

				if (tokenRes.networkError || tokenRes.httpError || tokenRes.status != AngryUser.TokengenStatus.OK)
					return result;

				return await VoteTask(bundleGuid, operation, true);
			}

			return result;
		}
		#endregion
	}
}
