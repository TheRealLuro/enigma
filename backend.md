# how to request to api
Replace anything in `{}` with the real value.
Example: `?map_name={map_name}` becomes `?map_name=randomname`.

# YOU CANNOT USE THESE APIS WITHOUT THE TOKEN I HAVE

base url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev`  
docs url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/docs`

---

## add new map
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/add?map_name={map_name}&seed={seed}&size={size}&difficulty={difficulty}&founder={founders_username}&time_completed={HH:MM:SS:MS}&first_rating={1_to_10}&token={token}`

gets:
- `map_name` (str)
- `seed` (str)
- `size` (int)
- `difficulty` (str)
- `founder` (str)
- `time_completed` (str, format `HH:MM:SS:MS`)
- `first_rating` (int 1-10)
- `token` (str)

steps:
1. Validates token.
2. Ensures map name and seed are unique.
3. Parses completion time.
4. Calculates map value with map audit.
5. Inserts map into maps collection.
6. Adds map id to founder `maps_discovered`.

returns:
```json
{
  "status": "success",
  "map_id": "698ebfa6b6656fe9ffadd1bc",
  "maps_discovered_updated": 1
}
```

---

## get leaderboard
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/leaderboard/leaderboard?token={token}&sort_by={sorter}&order={asc_or_desc}`

can sort by:
- `rating`
- `plays`
- `best_time`
- `time_founded`
- `difficulty`
- `map_name`

gets:
- `token` (str)
- `sort_by` (str)
- `order` (`asc` or `desc`)

steps:
1. Validates token.
2. Validates sort field.
3. Aggregates map list and sorts it.
4. Serializes `_id` fields.

returns:
```json
{
  "maps": [
    {
      "_id": "698eb80f89cfaf05533967ee",
      "map_name": "tonyo",
      "seed": "test",
      "size": 9,
      "difficulty": "e",
      "founder": "luro"
    }
  ]
}
```

---

## login
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/login?username={username}&passwd={password}&token={token}`

gets:
- `username` (str)
- `passwd` (str)
- `token` (str)

steps:
1. Validates token.
2. Finds user by username.
3. Verifies bcrypt password.
4. Checks `last_login_at` for daily reward.
5. Updates `last_login_at`.
6. Adds daily reward once per UTC day.
7. Returns user data without password.

returns:
```json
{
  "status": "success",
  "user": {
    "username": "luro",
    "maze_nuggets": 500
  },
  "daily_reward_granted": true,
  "daily_reward_amount": 50
}
```

---

## sign up
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/signup?username={username}&email={email}&passwd={password}&token={token}`

gets:
- `username` (str)
- `email` (str)
- `passwd` (str)
- `token` (str)

steps:
1. Validates token.
2. Checks username uniqueness.
3. Hashes password with bcrypt.
4. Creates default user profile/stat/economy fields.

returns:
```json
{
  "status": "success",
  "user_id": "698ebfcbb6656fe9ffadd1bd"
}
```

---

## update map
method: `PUT`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/update_map?seed={seed}&username={username}&completion_time={HH:MM:SS:MS}&token={token}&rating={1_to_10}`

gets:
- `seed` (str)
- `username` (str)
- `completion_time` (str)
- `token` (str)
- `rating` (int, optional)

steps:
1. Validates token.
2. Finds map by seed.
3. Increments play count.
4. Pushes rating if provided.
5. Compares best time and updates best-time owner if faster.

returns:
```json
{
  "status": "success"
}
```

---

## update user stats + reward payout
method: `PUT`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/update_progress?username={username}&map_seed={seed_played}&items_in_use={comma_separated_item_ids_or_none}&earned_mn={base_reward_int}&token={token}&seed_existed={true_or_false}&map_lost={true_or_false}`

gets:
- `username` (str)
- `map_seed` (str)
- `items_in_use` (str, example: `reward_magnet,puzzle_skip` or `none`)
- `earned_mn` (int, base reward before multipliers)
- `token` (str)
- `seed_existed` (bool, default `true`)
- `map_lost` (bool, default `false`)

steps:
1. Validates token.
2. Validates user and map.
3. Parses `items_in_use`.
4. Validates user owns enough quantity of all used items.
5. Reads item effects from `item_inventory`.
6. Applies reward multipliers to `earned_mn`.
7. Adds final reward to `maze_nuggets`.
8. Increments progress stats (`played`, `completed` or `lost`).
9. Consumes used items from `item_counts`.
10. Adds map to `maps_discovered` if `seed_existed=false`.

returns:
```json
{
  "status": "success",
  "modified_count": 1,
  "rewarded_mn": 125,
  "reward_multiplier": 1.25
}
```

---

## generate seed
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/maze/genseed?difficulty={difficulty}&size={size}&token={token}`

gets:
- `difficulty` (str)
- `size` (int, must be > 1)
- `token` (str)

steps:
1. Validates token.
2. Validates size.
3. Generates seed until a unique one is found.
4. Returns seed prefixed with difficulty.

returns:
```json
{
  "seed": "E-0,0DtN-1,0FxN-2,0HzN-2,1LuN-1,1KvR-0,1GwN-0,2CyN-1,2HuF-2,2AwS"
}
```

---

## get user profile
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/getuser?username={username}&passwd={password}&token={token}`

gets:
- `username` (str)
- `passwd` (str)
- `token` (str)

steps:
1. Validates token.
2. Loads user.
3. Verifies password.
4. Expands `maps_discovered` ids into map docs.
5. Removes password from response.

returns:
```json
{
  "status": "success",
  "user": {
    "username": "luro",
    "maps_discovered": [
      {
        "map_name": "map1"
      }
    ]
  }
}
```

---

## load map
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/load_map?map_name={map_name}&token={token}`

gets:
- `map_name` (str)
- `token` (str)

steps:
1. Validates token.
2. Finds map by name.
3. Returns seed.

returns:
```json
{
  "status": "success",
  "seed": "..."
}
```

---

## send friend request
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/send_fr?sender_user={your_username}&receiver_user={target_username}&token={token}`

gets:
- `sender_user` (str)
- `receiver_user` (str)
- `token` (str)

steps:
1. Validates token.
2. Blocks self-add.
3. Validates receiver exists.
4. Prevents duplicate pending request.
5. Adds sender to receiver `friend_requests`.

returns:
```json
{
  "status": "Friend request sent!"
}
```

---

## accept friend request
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/accept_fr?username={your_username}&adding={target_username}&token={token}`

gets:
- `username` (str)
- `adding` (str)
- `token` (str)

steps:
1. Validates token.
2. Validates both users exist.
3. Blocks self-add.
4. Validates pending request exists.
5. Adds both users to each other `friends`.
6. Removes pending request from both sides.

returns:
```json
{
  "status": "Friend request accepted!"
}
```

---

## add map to marketplace
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/add_to_marketplace?map_name={map_name}&price={price}&token={token}`

gets:
- `map_name` (str)
- `price` (int)
- `token` (str)

steps:
1. Validates token.
2. Loads map by name.
3. Builds listing metadata.
4. Inserts listing into marketplace collection.

returns:
```json
{
  "Success": "You added the listing to the marketplace, You listed for {x}x the value and {y}x the sold last value"
}
```

---

## buy from marketplace
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/marketplace/buy?map_name={map_name}&buyer={buyer_username}&token={token}`

gets:
- `map_name` (str)
- `buyer` (str)
- `token` (str)

steps:
1. Validates token.
2. Atomically claims listing (`find_one_and_delete`).
3. Blocks self-buy.
4. Validates buyer/seller/map.
5. Atomically debits buyer balance.
6. Credits seller balance.
7. Transfers map ownership.
8. Updates `sold_for_last` and `last_bought`.

returns:
```json
{
  "status": "success"
}
```

---

## get item shop
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/merchant/items`

gets:
- no query params

steps:
1. Reads all current docs from `item_shop`.
2. Converts Mongo `_id` to string.

returns:
```json
{
  "items": [
    {
      "_id": "mongo_id_as_string",
      "item_id": "flashlight_basic",
      "price": 50,
      "stock": 25
    }
  ]
}
```

---

## buy merchant item
method: `POST`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/merchant/buy_item?username={username}&item_id={item_id}&quantity={quantity}&token={token}`

gets:
- `username` (str)
- `item_id` (str)
- `quantity` (int, default `1`)
- `token` (str)

steps:
1. Validates token and quantity.
2. Starts Mongo transaction.
3. Validates shop item and user.
4. Decrements item stock in `item_shop`.
5. Decrements item stock in `item_inventory`.
6. For cosmetics: charge + one-time ownership add atomically.
7. For non-cosmetics: charge + increment `item_counts.<item_id>` atomically.
8. Commits transaction, or rolls back on any failure.

returns:
```json
{
  "status": "success",
  "item_id": "flashlight_basic",
  "quantity": 1,
  "total_cost": 50
}
```

---

## protected docs
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/docs`

gets:
- no query params

steps:
1. Verifies caller IP is allowed.
2. Returns Swagger UI page.

returns:
- Swagger UI HTML (not JSON)

---

## openapi schema
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/openapi.json`

gets:
- no query params

steps:
1. Builds OpenAPI schema from app routes.
2. Returns schema JSON.

returns:
- OpenAPI JSON document

---

## get my ip
method: `GET`  
request url: `https://nonelastic-prorailroad-gillian.ngrok-free.dev/what-is-my-ip`

gets:
- no query params

steps:
1. Reads `x-forwarded-for` if present.
2. Falls back to `request.client.host`.
3. Returns both values.

returns:
```json
{
  "real_ip": "x.x.x.x",
  "client_host": "x.x.x.x"
}
```
