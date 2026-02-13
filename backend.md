
# how to request to api.
replace anything with {} with that value by itself, so ?map_name={map_name} becomes ?map_name=randomname

docs url: https://https://nonelastic-prorailroad-gillian.ngrok-free.dev/docs

## add new map 
https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/add?map_name={map_name}&seed={seed}&size={size}&difficulty={difficulty}&founder={founders username}&time_completed={formatted 00:00:00:00}first_rating={rating}

returns 
{
  "status": "success",
  "map_id": "698ebfa6b6656fe9ffadd1bc",
  "maps_discovered_updated": 0
}


## get leaderboard 
can sort by {
    "rating",
    "plays",
    "best_time",
    "time_founded",
    "difficulty",
    "map_name"
}

https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/leaderboard/leaderboard?sort_by={sorter}&order={order}

returns
{
  "maps": [
    {
      "_id": "698eb80f89cfaf05533967ee",
      "map_name": "tonyo",
      "seed": "test",
      "size": 9,
      "difficulty": "e",
      "founder": "luro",
      "time_founded": "2026-02-13T05:35:11.740000",
      "best_time": {
        "hours": 0,
        "minutes": 0,
        "seconds": 0,
        "milliseconds": 0
      },
      "user_with_best_time": "luro",
      "rating": 0,
      "plays": 1
    }
  ]
}



## login 

request url: https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/login?username={username}&passwd={password}

returns 
{
  "status": "success",
  "user": {
    "_id": "698ea4ac428524e4d1a8e705",
    "username": "luro",
    "maps_discovered": [
      "698ea21986a2f27708de2c35",
      "698ea676bc766895fcd368e0",
      "698eb80f89cfaf05533967ee"
    ],
    "number_of_maps_played": 2,
    "maps_completed": 0,
    "maps_lost": 2
  }
}


## Sign up
request url: https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/new?username={username}&passwd={password}

return 
{
  "status": "success",
  "user_id": "698ebfcbb6656fe9ffadd1bd"
}



## Update map (updates best time if needed and adds the rating)
!! ONLY CALL THIS IF THE SEED IS LOADED OR EXISTS !!

request url: https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/maps/update_map?seed={seed}&username={username}&completion_time={formatted 00:00:00:00}rating={rating}

returns 
{
  "status": "success"
}


## update user stats

request url: https://nonelastic-prorailroad-gillian.ngrok-free.dev/database/users/update_progress?username={username}&map_seed={seed they just played}&seed_existed={did it exist, true or false}&map_lost={did they win, true or false}

returns
{
  "status": "success",
  "modified_count": 1
}


## Generate seed

request url: https://nonelastic-prorailroad-gillian.ngrok-free.dev/maze/genseed?difficulty={difficulty}&size={size}

returns:
{
  "seed": "E-0,0DtN-1,0FxN-2,0HzN-2,1LuN-1,1KvR-0,1GwN-0,2CyN-1,2HuF-2,2AwS"
}