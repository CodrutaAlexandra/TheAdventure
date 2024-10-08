using System.Runtime.InteropServices;
using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.SDL;
using TheAdventure.Models;
using TheAdventure.Models.Data;

namespace TheAdventure
{
    public class Engine
    {
        private readonly Dictionary<int, GameObject> _gameObjects = new();
        private readonly Dictionary<string, TileSet> _loadedTileSets = new();

        private Level? _currentLevel;
        private PlayerObject? _player;
        private GameRenderer _renderer;
        private Input _input;

        private DateTimeOffset _lastUpdate = DateTimeOffset.Now;
        private DateTimeOffset _lastPlayerUpdate = DateTimeOffset.Now;

        public Engine(GameRenderer renderer, Input input)
        {
            _renderer = renderer;
            _input = input;

            _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
        }

        public void InitializeWorld()
        {
            var jsonSerializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
            var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));

            var level = JsonSerializer.Deserialize<Level>(levelContent, jsonSerializerOptions);
            if (level == null) return;
            foreach (var refTileSet in level.TileSets)
            {
                var tileSetContent = File.ReadAllText(Path.Combine("Assets", refTileSet.Source!));
                if (!_loadedTileSets.TryGetValue(refTileSet.Source!, out var tileSet))
                {
                    tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent, jsonSerializerOptions);

                    if (tileSet == null) continue;
                    foreach (var tile in tileSet.Tiles)
                    {
                        var internalTextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                        tile.InternalTextureId = internalTextureId;
                    }

                    _loadedTileSets[refTileSet.Source!] = tileSet;
                }

                refTileSet.Set = tileSet;
            }

            _currentLevel = level;
            /*SpriteSheet spriteSheet = new(_renderer, Path.Combine("Assets", "player.png"), 10, 6, 48, 48, new FrameOffset() { OffsetX = 24, OffsetY = 42 });
            spriteSheet.Animations["IdleDown"] = new SpriteSheet.Animation()
            {
                StartFrame = new FramePosition(),//(0, 0),
                EndFrame = new FramePosition() { Row = 0, Col = 5 },
                DurationMs = 1000,
                Loop = true
            };
            */
            var spriteSheet = SpriteSheet.LoadSpriteSheet("player.json", "Assets", _renderer);
            if(spriteSheet != null){
                _player = new PlayerObject(spriteSheet, 100, 100);
            }
            _renderer.SetWorldBounds(new Rectangle<int>(0, 0, _currentLevel.Width * _currentLevel.TileWidth,
                _currentLevel.Height * _currentLevel.TileHeight));
        }

        private bool canMoveInDirection(bool direction, Tile?  tile, Tile? tileHead){
            if (direction && (tile?.Solid == true || tileHead?.Solid == true))
            {
                return false;
            }

            return direction;
        }

        public void ProcessFrame()
        {
            var currentTime = DateTimeOffset.Now;
            var secsSinceLastFrame = (currentTime - _lastUpdate).TotalSeconds;
            _lastUpdate = currentTime;

            bool up = _input.IsUpPressed();
            bool down = _input.IsDownPressed();
            bool left = _input.IsLeftPressed();
            bool right = _input.IsRightPressed();

            // if(_currentLevel != null && _player != null)
            // {
                for (var layer = 0; layer < _currentLevel?.Layers.Length; ++layer)
                {
                    var aproximatedPixelsToMove = 3;
                    var blockSize = 16;
                    var cLayer = _currentLevel.Layers[layer];

                    // #verySmartAndLongArithmeticCalculations
                    var cTileUp = GetTile(cLayer.Data[(_player!.Position.Y - aproximatedPixelsToMove - blockSize) / blockSize * cLayer.Width + _player.Position.X / blockSize] - 1);
                    var cTileRight = GetTile(cLayer.Data[_player.Position.Y / blockSize * cLayer.Width + (_player.Position.X + (int)(aproximatedPixelsToMove * 2)) / blockSize] - 1);
                    var cTileRightHead = GetTile(cLayer.Data[(_player.Position.Y - blockSize) / blockSize * cLayer.Width + (_player.Position.X + (int)(aproximatedPixelsToMove * 2)) / blockSize] - 1);
                    var cTileDown = GetTile(cLayer.Data[(_player.Position.Y + aproximatedPixelsToMove) / blockSize * cLayer.Width + _player.Position.X / blockSize] - 1);
                    var cTileLeft = GetTile(cLayer.Data[_player.Position.Y / blockSize * cLayer.Width + (_player.Position.X - aproximatedPixelsToMove) / blockSize] - 1);
                    var cTileLeftHead = GetTile(cLayer.Data[(_player.Position.Y - blockSize) / blockSize * cLayer.Width + (_player.Position.X - aproximatedPixelsToMove) / blockSize] - 1);

                    up = canMoveInDirection(up, cTileUp, null);
                    right = canMoveInDirection(right, cTileRight, cTileRightHead);
                    down = canMoveInDirection(down, cTileDown, null);
                    left = canMoveInDirection(left, cTileLeft, cTileLeftHead);
                }

                _player?.UpdatePlayerPosition(up ? 1.0 : 0.0, down ? 1.0 : 0.0, left ? 1.0 : 0.0, right ? 1.0 : 0.0,
                    _currentLevel!.Width * _currentLevel!.TileWidth, _currentLevel!.Height * _currentLevel!.TileHeight,
                    secsSinceLastFrame);
            // }


            var itemsToRemove = new List<int>();
            itemsToRemove.AddRange(GetAllTemporaryGameObjects().Where(gameObject => gameObject.IsExpired)
                .Select(gameObject => gameObject.Id).ToList());

            foreach (var gameObject in itemsToRemove)
            {
                _gameObjects.Remove(gameObject);
            }
        }

        public void RenderFrame()
        {
            _renderer.SetDrawColor(0, 0, 0, 255);
            _renderer.ClearScreen();

            if(_player != null)
            {
                _renderer.CameraLookAt(_player.Position.X, _player.Position.Y);
            }

            RenderTerrain();
            RenderAllObjects();

            _renderer.PresentFrame();
        }

        private Tile? GetTile(int id)
        {
            if (_currentLevel == null) return null;
            foreach (var tileSet in _currentLevel.TileSets)
            {
                foreach (var tile in tileSet.Set!.Tiles)
                {
                    if (tile.Id == id)
                    {
                        return tile;
                    }
                }
            }

            return null;
        }

        private void RenderTerrain()
        {
            if (_currentLevel == null) return;
            for (var layer = 0; layer < _currentLevel.Layers.Length; ++layer)
            {
                var cLayer = _currentLevel.Layers[layer];

                for (var i = 0; i < _currentLevel.Width; ++i)
                {
                    for (var j = 0; j < _currentLevel.Height; ++j)
                    {
                        var cTileId = cLayer.Data[j * cLayer.Width + i] - 1;
                        var cTile = GetTile(cTileId);
                        if (cTile == null) continue;

                        var src = new Rectangle<int>(0, 0, cTile.ImageWidth, cTile.ImageHeight);
                        var dst = new Rectangle<int>(i * cTile.ImageWidth, j * cTile.ImageHeight, cTile.ImageWidth,
                            cTile.ImageHeight);

                        _renderer.RenderTexture(cTile.InternalTextureId, src, dst);
                    }
                }
            }
        }

        private IEnumerable<RenderableGameObject> GetAllRenderableObjects()
        {
            foreach (var gameObject in _gameObjects.Values)
            {
                if (gameObject is RenderableGameObject renderableGameObject)
                {
                    yield return renderableGameObject;
                }
            }
        }

        private IEnumerable<TemporaryGameObject> GetAllTemporaryGameObjects()
        {
            foreach (var gameObject in _gameObjects.Values)
            {
                if (gameObject is TemporaryGameObject temporaryGameObject)
                {
                    yield return temporaryGameObject;
                }
            }
        }

        private void RenderAllObjects()
        {
            foreach (var gameObject in GetAllRenderableObjects())
            {
                gameObject.Render(_renderer);
            }

            _player?.Render(_renderer);
        }

        private void AddBomb(int x, int y)
        {
            var translated = _renderer.TranslateFromScreenToWorldCoordinates(x, y);
           
            var spriteSheet = SpriteSheet.LoadSpriteSheet("bomb.json", "Assets", _renderer);
            if(spriteSheet != null){
                spriteSheet.ActivateAnimation("Explode");
                TemporaryGameObject bomb = new(spriteSheet, 2.1, (translated.X, translated.Y));
                _gameObjects.Add(bomb.Id, bomb);
                SoundManager explosionSound = new SoundManager("C:\\Users\\Codruta Alexandra\\OneDrive\\OneDrive - e-uvt.ro\\Documents\\TheAdventure\\Assets\\bomb_exploding.mp3");
                explosionSound.Play();;
            }
        }
    }
}