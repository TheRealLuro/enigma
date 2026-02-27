# Enigma API (Sanitized Public Documentation)

> ⚠️ This is a sanitized public version of the Enigma API.
> Real infrastructure details, tokens, and private endpoints have been removed.

---

## Base URL

```
https://api.example.com
```

---

## add new map

**method:** POST
**request url:**

```
https://api.example.com/database/maps/add?map_name={map_name}&seed={seed}&size={size}&difficulty={difficulty}&founder={founders_username}&time_completed={HH:MM:SS:MS}&first_rating={1_to_10}
```

**params:**

* `map_name` (str)
* `seed` (str)
* `size` (int)
* `difficulty` (str)
* `founder` (str)
* `time_completed` (str, format `HH:MM:SS:MS`)
* `first_rating` (int 1-10)

---

## get leaderboard

**method:** GET
**request url:**

```
https://api.example.com/database/leaderboard/leaderboard?sort_by={sorter}&order={asc_or_desc}
```

**sort options:**

* `rating`
* `plays`
* `best_time`
* `time_founded`
* `difficulty`
* `map_name`

---

## login

**method:** POST
**request url:**

```
https://api.example.com/database/users/login?username={username}&passwd={password}
```

---

## sign up

**method:** POST
**request url:**

```
https://api.example.com/database/users/signup?username={username}&email={email}&passwd={password}
```

---

## update map

**method:** PUT
**request url:**

```
https://api.example.com/database/maps/update_map?seed={seed}&username={username}&completion_time={HH:MM:SS:MS}&rating={1_to_10}
```

---

## update user stats + reward payout

**method:** PUT
**request url:**

```
https://api.example.com/database/users/update_progress?username={username}&map_seed={seed_played}&items_in_use={comma_separated_item_ids_or_none}&earned_mn={base_reward_int}&seed_existed={true_or_false}&map_lost={true_or_false}
```

---

## generate seed

**method:** GET
**request url:**

```
https://api.example.com/maze/genseed?difficulty={difficulty}&size={size}
```

---

## get user profile

**method:** GET
**request url:**

```
https://api.example.com/database/users/getuser?username={username}&passwd={password}
```

---

## load map

**method:** GET
**request url:**

```
https://api.example.com/database/maps/load_map?map_name={map_name}
```

---

## send friend request

**method:** POST
**request url:**

```
https://api.example.com/database/users/send_fr?sender_user={your_username}&receiver_user={target_username}
```

---

## accept friend request

**method:** POST
**request url:**

```
https://api.example.com/database/users/accept_fr?username={your_username}&adding={target_username}
```

---

## add map to marketplace

**method:** POST
**request url:**

```
https://api.example.com/database/maps/add_to_marketplace?map_name={map_name}&price={price}
```

---

## buy from marketplace

**method:** POST
**request url:**

```
https://api.example.com/database/marketplace/buy?map_name={map_name}&buyer={buyer_username}
```

---

## get item shop

**method:** GET
**request url:**

```
https://api.example.com/database/merchant/items
```

---

## buy merchant item

**method:** POST
**request url:**

```
https://api.example.com/database/merchant/buy_item?username={username}&item_id={item_id}&quantity={quantity}
```

---

## Notes

* This documentation is intentionally sanitized for public release.
* Authentication, rate limiting, and infrastructure protections are omitted.
* Internal implementation details are not included in this version.
