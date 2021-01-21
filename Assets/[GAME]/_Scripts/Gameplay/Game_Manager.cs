﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Game_Manager : MonoBehaviour
{
	[SerializeField] private CanvasScaler canvas;
	[SerializeField] private float minDragDistance = 30;
	public int minMatchCount = 3;

	public System.Action<List<GamePiece>> matchFound;

	private Board board;
	private GamePiece selectedPiece;
	private Vector2 touchPos;
	private List<List<GamePiece>> matches = new List<List<GamePiece>>();

	private void Awake()
	{
		board = FindObjectOfType<Board>();

		board.pieceCreatedEvent += OnPieceCreated;
		board.pieceDestroyedEvent += OnPieceDestroyed;
	}

	private void Start()
	{
		//Since the minDragDistance is based on pixels, different resolutions will result in different distances, so we recalculate the distance based on the current resolution
		minDragDistance = (Screen.width * minDragDistance) / canvas.referenceResolution.x; //TODO check if this is right

		board.GenerateBoard();
		//TODO testar gerando o board 2 vezes depois que tudo estiver terminado
	}

	private void Update()
	{
		//If we have a piece selected and then click outside the board, deselect the piece
		if (Mouse.current.leftButton.wasPressedThisFrame)
		{
			if (selectedPiece != null && !RectTransformUtility.RectangleContainsScreenPoint(board.piecesParent, Mouse.current.position.ReadValue()))
			{
				selectedPiece = null;
			}
		}
	}

	private void OnDestroy()
	{
		if (board == null)
			return;

		board.pieceCreatedEvent -= OnPieceCreated;
		board.pieceDestroyedEvent -= OnPieceDestroyed;
	}

	private void OnPieceCreated(GamePiece p)
	{
		p.touchedPieceEvent += PieceWasSelected;
		p.releasedPieceEvent += PieceWasReleased;
	}

	private void OnPieceDestroyed(GamePiece p)
	{
		p.touchedPieceEvent -= PieceWasSelected;
		p.releasedPieceEvent -= PieceWasReleased;
	}

	private void PieceWasSelected(GamePiece p)
	{
		//If we selected the same piece twice, we deselect it by clearing its reference
		if (p == selectedPiece)
		{
			selectedPiece = null;
			return;
		}

		//If we don't have any piece selected at the moment, we select it
		if (selectedPiece == null)
		{
			selectedPiece = p;
			//TODO change this to use the input system actions instead...?
			touchPos = Mouse.current.position.ReadValue();
			return;
		}

		//If we selected a different piece than the current selcted one, we do a swap
		StartCoroutine(SwapPiecesAndCheckForMatches(selectedPiece, p));
	}

	private void PieceWasReleased(GamePiece p)
	{
		if (selectedPiece == null)
			return;

		Vector2 releasePos = Mouse.current.position.ReadValue();
		Vector2 direction = releasePos - touchPos;

		//If the distance between the click down and the click up is greater than the minDragDistance, we consider that we want to swap the pieces with a swipe
		if (direction.magnitude > minDragDistance)
		{
			Vector2Int targetPos = p.boardPos;

			//Check the direction we want to swap
			if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
			{
				if (direction.x > 0)
				{
					targetPos.x += 1; //Right
				}
				else
				{
					targetPos.x -= 1; //Left
				}
			}
			else
			{
				if (direction.y > 0)
				{
					targetPos.y += 1; //Up
				}
				else
				{
					targetPos.y -= 1; //Down
				}
			}

			//With the direction, we try to find the piece we want to swap with
			GamePiece targetPiece = null;

			if (targetPos.x >= 0 &&
				targetPos.y >= 0 &&
				targetPos.x < board.boardSize.x &&
				targetPos.y < board.boardSize.y)
			{
				targetPiece = board.grid[targetPos.x, targetPos.y];
			}

			//If we found the piece we want to swap places, do the swap. Otherwise, deselect the piece
			if (targetPiece != null)
			{
				StartCoroutine(SwapPiecesAndCheckForMatches(p, targetPiece));
			}
			else
			{
				selectedPiece = null;
				Debug.Log("Stuck"); //TODO maybe do a "stuck" animation when trying to swap to outside the board?
			}
		}
	}

	private IEnumerator SwapPiecesAndCheckForMatches(GamePiece p1, GamePiece p2)
	{
		board.SwapPieces(p1, p2);

		//Animate the visuals
		p1.AnimateImagePositionOnGrid(p2.boardPos, p1.boardPos, board.swapDuration, board.swapCurve);
		p2.AnimateImagePositionOnGrid(p1.boardPos, p2.boardPos, board.swapDuration, board.swapCurve);

		//After a swap is executed (successfully or not), we clear the selected piece
		selectedPiece = null;

		yield return new WaitForSeconds(board.swapDuration);

		//If after the swipe a match was NOT found, we return the pieces to their original positions
		if (CheckForMatches() == 0)
		{
			board.SwapPieces(p1, p2);

			//Animate the visuals
			p1.AnimateImagePositionOnGrid(p2.boardPos, p1.boardPos, board.swapDuration, board.swapCurve);
			p2.AnimateImagePositionOnGrid(p1.boardPos, p2.boardPos, board.swapDuration, board.swapCurve);

			yield return new WaitForSeconds(board.swapDuration);
			yield break;
		}

		//If at least one match WAS found, we make a loop removing all matches, until there's no more matches to remove
		do
		{
			//Destroy all matched pieces
			foreach (var match in matches)
			{
				//If this match has a piece that was already removed, that means this match was part of another match,
				//so we have a "cross match", which we can use to give bonus points or something
				if (match.Any(p => board.grid[p.boardPos.x, p.boardPos.y] == null))
				{
					Debug.Log($"Cross Match! Adding {match.Count * 2} points");
					matchFound?.Invoke(match);
				}
				else
				{
					Debug.Log($"Adding {match.Count} points");
					matchFound?.Invoke(match);
				}

				for (int i = 0; i < match.Count; i++)
				{
					board.RemovePiece(match[i]);
				}
			}

			yield return new WaitForSeconds(board.fallDuration);

			//Drop the remaining pieces down to fill the gaps left by the removed pieces
			for (int x = 0; x < board.boardSize.x; x++)
			{
				for (int y = 1; y < board.boardSize.y; y++)
				{
					GamePiece p = board.grid[x, y];

					if(p != null)
					{
						Vector2Int startPos = new Vector2Int(x, y);
						board.DropPiece(p);

						if (startPos != p.boardPos)
						{
							p.AnimateImagePositionOnGrid(startPos, p.boardPos, board.fallDuration, board.fallCurve);
						}
					}
				}
			}

			//Add new random pieces in the remaining empty spaces
			for (int x = 0; x < board.boardSize.x; x++)
			{
				for (int y = 1; y < board.boardSize.y; y++)
				{
					if (board.grid[x, y] == null)
						board.CreatePieceAtPosition(board.availablePieces[Random.Range(0, board.availablePieces.Length)], x, y);
				}
			}

			yield return new WaitForSeconds(board.fallDuration * 2);
		}
		while (CheckForMatches() > 0);
	}

	private int CheckForMatches()
	{
		matches.Clear();

		for (int i = 0; i < board.boardSize.x; i++)
		{
			for (int j = 0; j < board.boardSize.y; j++)
			{
				CheckSurroundingPieces(board.grid[i, j]);
			}
		}

		//Did we find any macth?
		return matches.Count;
	}

	private void CheckSurroundingPieces(GamePiece p)
	{
		//Check all valid directions
		for (int x = -1; x <= 1; x++)
		{
			for (int y = -1; y <= 1; y++)
			{
				if (Mathf.Abs(x) + Mathf.Abs(y) == 1) //TODO If we remove this condition, we can check for the diagonal pieces too!
				{
					List<GamePiece> currentMatch = new List<GamePiece>();
					CheckNextPiece(p, currentMatch, x, y);

					if (currentMatch.Count >= minMatchCount)
						matches.Add(currentMatch);
				}
			}
		}
	}

	private void CheckNextPiece(GamePiece p, List<GamePiece> currentMatchList, int dirX, int dirY)
	{
		//Add the current piece to the list
		currentMatchList.Add(p);

		//Check if the next piece in the defined direction exists
		GamePiece nextPiece = null;
		Vector2Int nextPos = new Vector2Int(p.boardPos.x + dirX, p.boardPos.y + dirY);

		if (nextPos.x >= 0 &&
			 nextPos.y >= 0 &&
			 nextPos.x < board.boardSize.x &&
			 nextPos.y < board.boardSize.y)
		{
			nextPiece = board.grid[nextPos.x, nextPos.y];
		}

		//If the next piece doesn't exist, we return
		if (nextPiece == null)
			return;

		//Check if this piece and the next are part of any existing matches, which would mean we're looking at pieces we already looked and in the same direction
		if (matches.Any(x => x.Contains(p) && x.Contains(nextPiece)))
			return;

		//If the next piece is of the same type as the one that was passed, we call this method again,
		//but passing the next piece instead, until we can't find any more similar/valid pieces
		if (nextPiece.typeID == p.typeID)
		{
			CheckNextPiece(nextPiece, currentMatchList, dirX, dirY);
		}
	}
}